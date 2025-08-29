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

            // Use DuckDuckGo Instant Answer API first, then fallback to HTML scraping
            var results = new List<SearchResult>();
            
            // Try DuckDuckGo Instant Answer API (free, no key required)
            var instantResults = await SearchInstantAnswerAsync(request.Query, cancellationToken);
            results.AddRange(instantResults);

            // If we need more results or got none, try HTML scraping approach
            if (results.Count < request.MaxResults)
            {
                var htmlResults = await SearchHtmlAsync(request.Query, request.MaxResults - results.Count, cancellationToken);
                results.AddRange(htmlResults);
            }

            // Filter by allowed domains if specified
            var domainsToFilter = request.AllowedDomains ?? _allowedDomains;
            if (domainsToFilter.Any())
            {
                results = results.Where(r => IsAllowedDomain(r.Domain, domainsToFilter)).ToList();
            }

            return results.Take(request.MaxResults).ToArray();
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
            // Create site-restricted query for allowed domains
            var siteQuery = query;
            if (_allowedDomains.Any())
            {
                var siteRestrictions = string.Join(" OR ", _allowedDomains.Select(d => $"site:{d}"));
                siteQuery = $"{query} ({siteRestrictions})";
            }

            var encodedQuery = HttpUtility.UrlEncode(siteQuery);
            var url = $"https://duckduckgo.com/html/?q={encodedQuery}";

            var response = await _httpClient.GetStringAsync(url, cancellationToken);
            
            // Simple HTML parsing to extract search results
            return ParseDuckDuckGoHtml(response, maxResults);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DuckDuckGo HTML search failed for query: {Query}", query);
            return Array.Empty<SearchResult>();
        }
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