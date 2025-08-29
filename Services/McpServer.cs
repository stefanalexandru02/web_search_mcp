using Microsoft.Extensions.Logging;
using System.Text.Json;
using WebSearchMcp.Models;

namespace WebSearchMcp.Services;

public class McpServer
{
    private readonly ISearchService _searchService;
    private readonly IDomainFilterService _domainFilterService;
    private readonly ILogger<McpServer> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public McpServer(
        ISearchService searchService,
        IDomainFilterService domainFilterService,
        ILogger<McpServer> logger)
    {
        _searchService = searchService;
        _domainFilterService = domainFilterService;
        _logger = logger;
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting WebSearch MCP Server...");
        
        try
        {
            string? line;
            while ((line = await Console.In.ReadLineAsync()) != null && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogDebug("Received request: {Request}", line);
                    
                    var request = JsonSerializer.Deserialize<McpRequest>(line);
                    if (request == null)
                    {
                        _logger.LogWarning("Failed to deserialize request");
                        continue;
                    }

                    var response = await ProcessRequestAsync(request, cancellationToken);
                    var responseJson = JsonSerializer.Serialize(response, _jsonOptions);
                    
                    _logger.LogDebug("Sending response: {Response}", responseJson);
                    Console.WriteLine(responseJson);
                    await Console.Out.FlushAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing request: {Line}", line);
                    
                    var errorResponse = new McpResponse
                    {
                        JsonRpc = "2.0",
                        Error = new McpError
                        {
                            Code = -32603,
                            Message = "Internal error",
                            Data = ex.Message
                        }
                    };
                    
                    var errorJson = JsonSerializer.Serialize(errorResponse, _jsonOptions);
                    Console.WriteLine(errorJson);
                    await Console.Out.FlushAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Critical error in MCP server main loop");
        }
        
        _logger.LogInformation("WebSearch MCP Server stopped");
    }

    private async Task<McpResponse> ProcessRequestAsync(McpRequest request, CancellationToken cancellationToken)
    {
        return request.Method switch
        {
            "initialize" => await HandleInitializeAsync(request, cancellationToken),
            "tools/list" => HandleToolsList(request),
            "tools/call" => await HandleToolCallAsync(request, cancellationToken),
            "notifications/initialized" => HandleInitialized(request),
            _ => CreateErrorResponse(request.Id, -32601, $"Method not found: {request.Method}")
        };
    }

    private Task<McpResponse> HandleInitializeAsync(McpRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling initialize request");
        
        try
        {
            // Validate client capabilities if needed
            var initParams = JsonSerializer.Deserialize<InitializeParams>(
                JsonSerializer.Serialize(request.Params), _jsonOptions);

            _logger.LogInformation("Client: {Name} v{Version}", 
                initParams?.ClientInfo?.Name ?? "Unknown", 
                initParams?.ClientInfo?.Version ?? "Unknown");

            var result = new InitializeResult
            {
                ProtocolVersion = "2024-11-05",
                Capabilities = new ServerCapabilities
                {
                    Tools = new { }
                },
                ServerInfo = new ServerInfo
                {
                    Name = "WebSearch MCP Server",
                    Version = "1.0.0"
                }
            };

            return Task.FromResult(new McpResponse
            {
                Id = request.Id,
                Result = result
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during initialization");
            return Task.FromResult(CreateErrorResponse(request.Id, -32603, "Initialization failed"));
        }
    }

    private McpResponse HandleToolsList(McpRequest request)
    {
        _logger.LogInformation("Handling tools/list request");
        
        var tools = new[]
        {
            new Tool
            {
                Name = "web_search",
                Description = "Search the web for information, with optional domain filtering. Particularly useful for searching ftrack developer documentation and related resources.",
                InputSchema = new ToolInputSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, SchemaProperty>
                    {
                        ["query"] = new SchemaProperty
                        {
                            Type = "string",
                            Description = "The search query to execute"
                        },
                        ["allowed_domains"] = new SchemaProperty
                        {
                            Type = "array",
                            Description = "Optional array of domains to restrict search to (e.g., ['ftrack.com', 'github.com'])",
                            Items = new SchemaItems { Type = "string" }
                        },
                        ["max_results"] = new SchemaProperty
                        {
                            Type = "number",
                            Description = "Maximum number of results to return (default: 10)"
                        }
                    },
                    Required = new[] { "query" }
                }
            },
            new Tool
            {
                Name = "get_allowed_domains",
                Description = "Get the list of currently configured allowed domains for web search",
                InputSchema = new ToolInputSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, SchemaProperty>(),
                    Required = Array.Empty<string>()
                }
            },
            new Tool
            {
                Name = "search_ftrack_docs",
                Description = "Specialized search for ftrack developer documentation and API resources",
                InputSchema = new ToolInputSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, SchemaProperty>
                    {
                        ["query"] = new SchemaProperty
                        {
                            Type = "string",
                            Description = "The search query for ftrack documentation"
                        },
                        ["doc_type"] = new SchemaProperty
                        {
                            Type = "string",
                            Description = "Type of documentation to search",
                            Enum = new[] { "all", "api", "python-api", "rest-api", "javascript-api", "developer-guide" }
                        }
                    },
                    Required = new[] { "query" }
                }
            }
        };

        var result = new ToolListResult { Tools = tools };

        return new McpResponse
        {
            Id = request.Id,
            Result = result
        };
    }

    private async Task<McpResponse> HandleToolCallAsync(McpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var callParams = JsonSerializer.Deserialize<ToolCallParams>(
                JsonSerializer.Serialize(request.Params), _jsonOptions);

            if (callParams == null)
            {
                return CreateErrorResponse(request.Id, -32602, "Invalid tool call parameters");
            }

            _logger.LogInformation("Handling tool call: {ToolName}", callParams.Name);

            var result = callParams.Name switch
            {
                "web_search" => await HandleWebSearchAsync(callParams.Arguments, cancellationToken),
                "get_allowed_domains" => HandleGetAllowedDomains(),
                "search_ftrack_docs" => await HandleFtrackSearchAsync(callParams.Arguments, cancellationToken),
                _ => CreateErrorToolResult($"Unknown tool: {callParams.Name}")
            };

            return new McpResponse
            {
                Id = request.Id,
                Result = result
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling tool call");
            return CreateErrorResponse(request.Id, -32603, "Tool call failed");
        }
    }

    private async Task<ToolCallResult> HandleWebSearchAsync(Dictionary<string, object> arguments, CancellationToken cancellationToken)
    {
        try
        {
            var query = arguments.TryGetValue("query", out var q) ? q?.ToString() ?? "" : "";
            var maxResults = 10;
            if (arguments.TryGetValue("max_results", out var mr) && mr != null)
            {
                if (mr is JsonElement jsonElement)
                {
                    maxResults = jsonElement.GetInt32();
                }
                else
                {
                    maxResults = Convert.ToInt32(mr);
                }
            }

            string[]? allowedDomains = null;
            if (arguments.TryGetValue("allowed_domains", out var ad) && ad != null)
            {
                var domainsJson = JsonSerializer.Serialize(ad);
                allowedDomains = JsonSerializer.Deserialize<string[]>(domainsJson);
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return CreateErrorToolResult("Query parameter is required");
            }

            var searchRequest = new SearchRequest
            {
                Query = query,
                AllowedDomains = allowedDomains,
                MaxResults = maxResults
            };

            var results = await _searchService.SearchAsync(searchRequest, cancellationToken);
            
            var formattedResults = FormatSearchResults(results, query);

            return new ToolCallResult
            {
                Content = new[]
                {
                    new ToolContent
                    {
                        Type = "text",
                        Text = formattedResults
                    }
                },
                IsError = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in web search");
            return CreateErrorToolResult($"Search failed: {ex.Message}");
        }
    }

    private ToolCallResult HandleGetAllowedDomains()
    {
        var domains = _domainFilterService.GetAllowedDomains();
        var domainsText = domains.Any() 
            ? string.Join("\n- ", domains.Prepend("Allowed domains:"))
            : "No domain restrictions configured - all domains allowed";

        return new ToolCallResult
        {
            Content = new[]
            {
                new ToolContent
                {
                    Type = "text",
                    Text = domainsText
                }
            },
            IsError = false
        };
    }

    private async Task<ToolCallResult> HandleFtrackSearchAsync(Dictionary<string, object> arguments, CancellationToken cancellationToken)
    {
        try
        {
            var query = arguments.TryGetValue("query", out var q) ? q?.ToString() ?? "" : "";
            var docType = arguments.TryGetValue("doc_type", out var dt) ? dt?.ToString() ?? "all" : "all";

            if (string.IsNullOrWhiteSpace(query))
            {
                return CreateErrorToolResult("Query parameter is required");
            }

            // Create ftrack-specific search query
            var ftrackQuery = CreateFtrackQuery(query, docType);
            var ftrackDomains = new[] { "ftrack.com", "www.ftrack.com", "help.ftrack.com", "docs.ftrack.com", "developer.ftrack.com", "api.ftrack.com" };

            var searchRequest = new SearchRequest
            {
                Query = ftrackQuery,
                AllowedDomains = ftrackDomains,
                MaxResults = 10
            };

            var results = await _searchService.SearchAsync(searchRequest, cancellationToken);
            var formattedResults = FormatSearchResults(results, ftrackQuery, "ftrack developer documentation");

            return new ToolCallResult
            {
                Content = new[]
                {
                    new ToolContent
                    {
                        Type = "text",
                        Text = formattedResults
                    }
                },
                IsError = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ftrack search");
            return CreateErrorToolResult($"Ftrack search failed: {ex.Message}");
        }
    }

    private string CreateFtrackQuery(string query, string docType)
    {
        var baseQuery = $"site:ftrack.com {query}";
        
        return docType switch
        {
            "api" => $"{baseQuery} (API OR python-api OR rest-api OR javascript-api)",
            "python-api" => $"{baseQuery} python-api",
            "rest-api" => $"{baseQuery} rest-api",
            "javascript-api" => $"{baseQuery} javascript-api",
            "developer-guide" => $"{baseQuery} (developer OR guide OR tutorial)",
            _ => baseQuery
        };
    }

    private string FormatSearchResults(SearchResult[] results, string query, string? searchType = null)
    {
        if (!results.Any())
        {
            return $"No results found for query: '{query}'" + 
                   (searchType != null ? $" in {searchType}" : "");
        }

        var formatted = new List<string>
        {
            $"Search results for '{query}'" + (searchType != null ? $" in {searchType}" : "") + $" ({results.Length} results):",
            ""
        };

        foreach (var (result, index) in results.Select((r, i) => (r, i + 1)))
        {
            formatted.Add($"{index}. **{result.Title}**");
            formatted.Add($"   URL: {result.Url}");
            formatted.Add($"   Domain: {result.Domain}");
            if (!string.IsNullOrWhiteSpace(result.Snippet))
            {
                formatted.Add($"   Summary: {result.Snippet}");
            }
            formatted.Add("");
        }

        return string.Join("\n", formatted);
    }

    private McpResponse HandleInitialized(McpRequest request)
    {
        _logger.LogInformation("Client initialized notification received");
        
        // For notifications, we don't send a response
        return new McpResponse
        {
            Id = request.Id,
            Result = new { }
        };
    }

    private McpResponse CreateErrorResponse(object? id, int code, string message)
    {
        return new McpResponse
        {
            Id = id,
            Error = new McpError
            {
                Code = code,
                Message = message
            }
        };
    }

    private ToolCallResult CreateErrorToolResult(string message)
    {
        return new ToolCallResult
        {
            Content = new[]
            {
                new ToolContent
                {
                    Type = "text",
                    Text = $"Error: {message}"
                }
            },
            IsError = true
        };
    }
}