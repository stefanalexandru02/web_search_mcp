using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebSearchMcp.Models;

namespace WebSearchMcp.Services;

public interface IDomainFilterService
{
    bool IsAllowed(string domain);
    bool IsAllowed(string url, string[] customAllowedDomains);
    string[] GetAllowedDomains();
    SearchResult[] FilterResults(SearchResult[] results, string[]? customAllowedDomains = null);
}

public class DomainFilterService : IDomainFilterService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DomainFilterService> _logger;
    private readonly string[] _defaultAllowedDomains;

    public DomainFilterService(IConfiguration configuration, ILogger<DomainFilterService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _defaultAllowedDomains = _configuration.GetSection("AllowedDomains").Get<string[]>() ?? Array.Empty<string>();
        
        _logger.LogInformation("Domain filter initialized with {Count} allowed domains", _defaultAllowedDomains.Length);
    }

    public bool IsAllowed(string domain)
    {
        if (!_defaultAllowedDomains.Any())
        {
            _logger.LogDebug("No domain restrictions configured, allowing all domains");
            return true;
        }

        var isAllowed = _defaultAllowedDomains.Any(allowed =>
            domain.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith($".{allowed}", StringComparison.OrdinalIgnoreCase));

        _logger.LogDebug("Domain {Domain} is {Status}", domain, isAllowed ? "allowed" : "blocked");
        return isAllowed;
    }

    public bool IsAllowed(string url, string[] customAllowedDomains)
    {
        try
        {
            var uri = new Uri(url);
            var domain = uri.Host;

            if (!customAllowedDomains.Any())
            {
                return IsAllowed(domain);
            }

            var isAllowed = customAllowedDomains.Any(allowed =>
                domain.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
                domain.EndsWith($".{allowed}", StringComparison.OrdinalIgnoreCase));

            _logger.LogDebug("URL {Url} with domain {Domain} is {Status} for custom domain list", url, domain, isAllowed ? "allowed" : "blocked");
            return isAllowed;
        }
        catch (UriFormatException ex)
        {
            _logger.LogWarning(ex, "Invalid URL format: {Url}", url);
            return false;
        }
    }

    public string[] GetAllowedDomains()
    {
        return _defaultAllowedDomains.ToArray();
    }

    public SearchResult[] FilterResults(SearchResult[] results, string[]? customAllowedDomains = null)
    {
        var domainsToCheck = customAllowedDomains ?? _defaultAllowedDomains;
        
        if (!domainsToCheck.Any())
        {
            _logger.LogDebug("No domain filtering applied, returning all {Count} results", results.Length);
            return results;
        }

        var filteredResults = results
            .Where(result => IsAllowedByDomainList(result.Domain, domainsToCheck))
            .ToArray();

        _logger.LogInformation("Filtered {Original} results down to {Filtered} results using domain restrictions", 
            results.Length, filteredResults.Length);

        return filteredResults;
    }

    private bool IsAllowedByDomainList(string domain, string[] allowedDomains)
    {
        return allowedDomains.Any(allowed =>
            domain.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith($".{allowed}", StringComparison.OrdinalIgnoreCase));
    }
}