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

public class GoogleSearchResponse
{
    [JsonPropertyName("items")]
    public GoogleSearchItem[]? Items { get; set; }
    
    [JsonPropertyName("searchInformation")]
    public GoogleSearchInformation? SearchInformation { get; set; }
}

public class GoogleSearchItem
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
    
    [JsonPropertyName("link")]
    public string Link { get; set; } = string.Empty;
    
    [JsonPropertyName("snippet")]
    public string Snippet { get; set; } = string.Empty;
    
    [JsonPropertyName("displayLink")]
    public string DisplayLink { get; set; } = string.Empty;
}

public class GoogleSearchInformation
{
    [JsonPropertyName("totalResults")]
    public string TotalResults { get; set; } = string.Empty;
    
    [JsonPropertyName("searchTime")]
    public double SearchTime { get; set; }
}