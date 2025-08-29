using WebSearchMcp.Models;

namespace WebSearchMcp.Services;

public interface ISearchService
{
    Task<SearchResult[]> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}