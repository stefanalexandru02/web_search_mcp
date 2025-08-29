using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using WebSearchMcp.Models;

namespace WebSearchMcp.Services;

public class DuckDuckGoSearchService : ISearchService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DuckDuckGoSearchService> _logger;
    private readonly string[] _allowedDomains;

    public DuckDuckGoSearchService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<DuckDuckGoSearchService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        
        // Set user agent to avoid blocking
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
        
        _allowedDomains = _configuration.GetSection("AllowedDomains").Get<string[]>() ?? Array.Empty<string>();
    }

    public async Task<SearchResult[]> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Performing DuckDuckGo search for: {Query}", request.Query);

            var results = new List<SearchResult>();
            
            // Search with more results to allow for domain filtering
            var searchLimit = request.MaxResults * 3; // Get more results for filtering
            var htmlResults = await SearchHtmlAsync(request.Query, searchLimit, cancellationToken);
            results.AddRange(htmlResults);

            _logger.LogInformation("Retrieved {Count} raw search results", results.Count);

            // Filter by allowed domains if specified
            var domainsToFilter = request.AllowedDomains ?? _allowedDomains;
            if (domainsToFilter.Any())
            {
                var beforeCount = results.Count;
                results = results.Where(r => IsAllowedDomain(r.Domain, domainsToFilter)).ToList();
                _logger.LogInformation("After domain filtering: {Count} results (was {Before})", results.Count, beforeCount);
            }

            var finalResults = results.Take(request.MaxResults).ToArray();
            _logger.LogInformation("Returning {Count} search results", finalResults.Length);
            return finalResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing search for query: {Query}", request.Query);
            return Array.Empty<SearchResult>();
        }
    }

    private async Task<SearchResult[]> SearchInstantAnswerAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            var encodedQuery = HttpUtility.UrlEncode(query);
            var url = $"https://api.duckduckgo.com/?q={encodedQuery}&format=json&no_html=1&skip_disambig=1";

            var response = await _httpClient.GetStringAsync(url, cancellationToken);
            var data = JsonSerializer.Deserialize<DuckDuckGoInstantResponse>(response);

            var results = new List<SearchResult>();

            // Add related topics as search results
            if (data?.RelatedTopics != null)
            {
                foreach (var topic in data.RelatedTopics.Take(5))
                {
                    if (!string.IsNullOrEmpty(topic.FirstURL) && !string.IsNullOrEmpty(topic.Text))
                    {
                        var uri = new Uri(topic.FirstURL);
                        results.Add(new SearchResult
                        {
                            Title = ExtractTitleFromText(topic.Text),
                            Url = topic.FirstURL,
                            Snippet = topic.Text,
                            Domain = uri.Host
                        });
                    }
                }
            }

            return results.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DuckDuckGo Instant Answer API failed for query: {Query}", query);
            return Array.Empty<SearchResult>();
        }
    }

    private async Task<SearchResult[]> SearchHtmlAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        try
        {
            // Don't modify the query here - just search as-is
            var encodedQuery = HttpUtility.UrlEncode(query);
            var url = $"https://lite.duckduckgo.com/lite/?q={encodedQuery}";

            _logger.LogInformation("Searching DuckDuckGo for: {Query}", query);

            var response = await _httpClient.GetStringAsync(url, cancellationToken);
            
            // Parse the simpler lite version HTML
            return ParseDuckDuckGoLiteHtml(response, maxResults);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DuckDuckGo HTML search failed for query: {Query}", query);
            return Array.Empty<SearchResult>();
        }
    }

    private SearchResult[] ParseDuckDuckGoLiteHtml(string html, int maxResults)
    {
        var results = new List<SearchResult>();
        
        try
        {
            // Parse DuckDuckGo Lite results - simpler structure
            var resultPattern = @"<a rel=""nofollow"" href=""([^""]+)""[^>]*>([^<]+)</a>";
            var snippetPattern = @"<td class=""result-snippet"">([^<]+)</td>";
            
            var linkMatches = System.Text.RegularExpressions.Regex.Matches(html, resultPattern);
            var snippetMatches = System.Text.RegularExpressions.Regex.Matches(html, snippetPattern);

            _logger.LogInformation("Found {LinkCount} links and {SnippetCount} snippets", linkMatches.Count, snippetMatches.Count);

            for (int i = 0; i < Math.Min(linkMatches.Count, maxResults); i++)
            {
                var url = System.Web.HttpUtility.HtmlDecode(linkMatches[i].Groups[1].Value);
                var title = System.Web.HttpUtility.HtmlDecode(linkMatches[i].Groups[2].Value);
                
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    var snippet = i < snippetMatches.Count ? 
                        System.Web.HttpUtility.HtmlDecode(snippetMatches[i].Groups[1].Value) : "";

                    results.Add(new SearchResult
                    {
                        Title = title,
                        Url = url,
                        Snippet = snippet,
                        Domain = uri.Host
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse DuckDuckGo Lite HTML response");
        }

        return results.ToArray();
    }

    private SearchResult[] ParseDuckDuckGoHtml(string html, int maxResults)
    {
        var results = new List<SearchResult>();
        
        try
        {
            // Simple regex-based parsing (in production, consider using HtmlAgilityPack)
            var linkPattern = @"<a[^>]+href=""(/l/\?uddg=[^""]+)""[^>]*>([^<]+)</a>";
            var snippetPattern = @"<a[^>]+class=""result__snippet""[^>]*>([^<]+)</a>";
            
            var linkMatches = System.Text.RegularExpressions.Regex.Matches(html, linkPattern);
            var snippetMatches = System.Text.RegularExpressions.Regex.Matches(html, snippetPattern);

            for (int i = 0; i < Math.Min(linkMatches.Count, maxResults); i++)
            {
                var title = System.Web.HttpUtility.HtmlDecode(linkMatches[i].Groups[2].Value);
                var encodedUrl = linkMatches[i].Groups[1].Value;
                
                // Decode DuckDuckGo redirect URL
                var actualUrl = DecodeDuckDuckGoUrl(encodedUrl);
                
                if (Uri.TryCreate(actualUrl, UriKind.Absolute, out var uri))
                {
                    var snippet = i < snippetMatches.Count ? 
                        System.Web.HttpUtility.HtmlDecode(snippetMatches[i].Groups[1].Value) : "";

                    results.Add(new SearchResult
                    {
                        Title = title,
                        Url = actualUrl,
                        Snippet = snippet,
                        Domain = uri.Host
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse DuckDuckGo HTML response");
        }

        return results.ToArray();
    }

    private string DecodeDuckDuckGoUrl(string encodedUrl)
    {
        try
        {
            // Remove DuckDuckGo redirect prefix and decode
            var match = System.Text.RegularExpressions.Regex.Match(encodedUrl, @"uddg=([^&]+)");
            if (match.Success)
            {
                return HttpUtility.UrlDecode(match.Groups[1].Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode DuckDuckGo URL: {EncodedUrl}", encodedUrl);
        }
        
        return encodedUrl;
    }

    private string ExtractTitleFromText(string text)
    {
        // Extract title from text (usually the first part before dash or period)
        var parts = text.Split(new[] { " - ", ". " }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : text;
    }

    private bool IsAllowedDomain(string domain, string[] allowedDomains)
    {
        if (!allowedDomains.Any()) return true;

        return allowedDomains.Any(allowed => 
            domain.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith($".{allowed}", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("https://api.duckduckgo.com/?q=test&format=json", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

public class DuckDuckGoInstantResponse
{
    [JsonPropertyName("RelatedTopics")]
    public DuckDuckGoTopic[]? RelatedTopics { get; set; }
}

public class DuckDuckGoTopic
{
    [JsonPropertyName("FirstURL")]
    public string FirstURL { get; set; } = string.Empty;
    
    [JsonPropertyName("Text")]
    public string Text { get; set; } = string.Empty;
}