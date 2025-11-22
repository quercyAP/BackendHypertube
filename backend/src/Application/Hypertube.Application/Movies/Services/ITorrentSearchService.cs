using Hypertube.Application.Movies.DTOs;

namespace Hypertube.Application.Movies.Services;

public interface ITorrentSearchService
{
    Task<(List<TorrentSearchResultDto> Results, int TotalCount)> SearchAsync(string query, int page = 1, int limit = 20, CancellationToken cancellationToken = default);
    Task<(List<TorrentSearchResultDto> Results, int TotalCount)> GetPopularAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default);
}
