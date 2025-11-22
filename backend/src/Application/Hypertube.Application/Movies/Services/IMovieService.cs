using Hypertube.Application.Movies.DTOs;

namespace Hypertube.Application.Movies.Services;

public interface IMovieService
{
    Task<(List<MovieDto> Movies, int TotalCount)> GetPopularAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    
    Task<(List<MovieDto> Movies, int TotalCount)> SearchAsync(string query, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    
    Task<(List<MovieDto> Movies, int TotalCount)> GetByFiltersAsync(
        string? genre = null,
        int? year = null,
        decimal? minRating = null,
        string sortBy = "rating",
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<MovieDetailsDto?> GetMovieDetailsAsync(Guid id, CancellationToken cancellationToken = default);
}
