using System.Text.Json.Serialization;

namespace WebSearchMcp.Models;

public class SearchRequest
{
    public string Query { get; set; } = string.Empty;
    public string[]? AllowedDomains { get; set; }
    public int MaxResults { get; set; } = 10;
}

public class SearchResult
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
}