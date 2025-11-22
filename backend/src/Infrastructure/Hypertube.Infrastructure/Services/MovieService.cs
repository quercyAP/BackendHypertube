using Hypertube.Application.Movies.DTOs;
using Hypertube.Application.Movies.Services;
using Hypertube.Domain.Entities;
using Hypertube.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hypertube.Infrastructure.Services;

public class MovieService : IMovieService
{
    private readonly ITorrentSearchService _torrentSearchService;
    private readonly HypertubeDbContext _context;
    private readonly ILogger<MovieService> _logger;

    public MovieService(
        ITorrentSearchService torrentSearchService,
        HypertubeDbContext context,
        ILogger<MovieService> logger)
    {
        _torrentSearchService = torrentSearchService;
        _context = context;
        _logger = logger;
    }

    public async Task<(List<MovieDto> Movies, int TotalCount)> GetPopularAsync(
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting popular movies, page {Page}", page);

        // Get popular from torrent sources
        var (torrentResults, totalCount) = await _torrentSearchService.GetPopularAsync(page, pageSize, cancellationToken);

        // Store/update movies in database
        var movies = await StoreMoviesFromTorrentResults(torrentResults, cancellationToken);

        // Convert to DTOs
        var movieDtos = movies.Select(MapToDto).ToList();

        return (movieDtos, totalCount);
    }

    public async Task<(List<MovieDto> Movies, int TotalCount)> SearchAsync(
        string query,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Searching movies for query: {Query}, page {Page}", query, page);

        // Search torrent sources
        var (torrentResults, totalCount) = await _torrentSearchService.SearchAsync(query, page, pageSize, cancellationToken);

        // Store/update movies in database
        var movies = await StoreMoviesFromTorrentResults(torrentResults, cancellationToken);

        // Convert to DTOs
        var movieDtos = movies.Select(MapToDto).ToList();

        return (movieDtos, totalCount);
    }

    public async Task<(List<MovieDto> Movies, int TotalCount)> GetByFiltersAsync(
        string? genre = null,
        int? year = null,
        decimal? minRating = null,
        string sortBy = "rating",
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Getting filtered movies - Genre: {Genre}, Year: {Year}, MinRating: {MinRating}, SortBy: {SortBy}",
            genre, year, minRating, sortBy
        );

        // Query database for movies
        var query = _context.Movies.AsQueryable();

        // Apply filters
        if (!string.IsNullOrWhiteSpace(genre))
        {
            query = query.Where(m => m.Genre != null && m.Genre.Contains(genre));
        }

        if (year.HasValue)
        {
            query = query.Where(m => m.Year == year.Value);
        }

        if (minRating.HasValue)
        {
            query = query.Where(m => m.Rating >= minRating.Value);
        }

        // Apply sorting
        query = sortBy.ToLowerInvariant() switch
        {
            "title" => query.OrderBy(m => m.Title),
            "year" => query.OrderByDescending(m => m.Year),
            "rating" => query.OrderByDescending(m => m.Rating),
            _ => query.OrderByDescending(m => m.Rating)
        };

        // Get total count
        var totalCount = await query.CountAsync(cancellationToken);

        // Apply pagination
        var movies = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // If no results in DB, fallback to torrent search
        if (!movies.Any())
        {
            _logger.LogInformation("No movies in database matching filters, fetching from torrent sources");
            var (torrentResults, _) = await _torrentSearchService.GetPopularAsync(page, pageSize, cancellationToken);
            movies = await StoreMoviesFromTorrentResults(torrentResults, cancellationToken);
        }

        // Convert to DTOs
        var movieDtos = movies.Select(MapToDto).ToList();

        return (movieDtos, totalCount);
    }

    private async Task<List<Movie>> StoreMoviesFromTorrentResults(
        List<TorrentSearchResultDto> torrentResults,
        CancellationToken cancellationToken)
    {
        var movies = new List<Movie>();

        foreach (var result in torrentResults)
        {
            // Skip if no IMDb ID and no title
            if (string.IsNullOrWhiteSpace(result.ImdbId) && string.IsNullOrWhiteSpace(result.Title))
                continue;

            Movie? movie = null;

            // Try to find existing movie by IMDb ID
            if (!string.IsNullOrWhiteSpace(result.ImdbId))
            {
                movie = await _context.Movies
                    .FirstOrDefaultAsync(m => m.ImdbId == result.ImdbId, cancellationToken);
            }

            // If not found by IMDb ID, try by title and year
            if (movie == null && !string.IsNullOrWhiteSpace(result.Title) && result.Year.HasValue)
            {
                movie = await _context.Movies
                    .FirstOrDefaultAsync(m => m.Title == result.Title && m.Year == result.Year, cancellationToken);
            }

            // Create new movie if not found
            if (movie == null)
            {
                movie = new Movie
                {
                    Id = Guid.NewGuid(),
                    ImdbId = result.ImdbId,
                    Title = result.Title,
                    Year = result.Year,
                    Rating = result.Rating,
                    Genre = result.Genre,
                    Summary = result.Summary,
                    CoverImageUrl = result.CoverImageUrl,
                    Duration = result.Duration,
                    Director = result.Director,
                    Cast = result.Cast,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Movies.Add(movie);
            }
            else
            {
                // Update existing movie with latest info
                if (result.Rating.HasValue && result.Rating > 0)
                    movie.Rating = result.Rating;
                if (!string.IsNullOrWhiteSpace(result.Genre))
                    movie.Genre = result.Genre;
                if (!string.IsNullOrWhiteSpace(result.Summary))
                    movie.Summary = result.Summary;
                if (!string.IsNullOrWhiteSpace(result.CoverImageUrl))
                    movie.CoverImageUrl = result.CoverImageUrl;
                if (result.Duration.HasValue)
                    movie.Duration = result.Duration;
            }

            movies.Add(movie);
        }

        // Save changes to database
        if (movies.Any())
        {
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Stored/updated {Count} movies in database", movies.Count);
        }

        return movies;
    }

    public async Task<MovieDetailsDto?> GetMovieDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting movie details for ID: {MovieId}", id);

        // Fetch movie from database
        var movie = await _context.Movies.FindAsync(new object[] { id }, cancellationToken);

        if (movie == null)
        {
            _logger.LogWarning("Movie with ID {MovieId} not found", id);
            return null;
        }

        // Mark as watched by updating LastWatchedAt
        movie.LastWatchedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        // Get available torrents for this movie from external sources
        var availableTorrents = new List<TorrentOptionDto>();

        try
        {
            // Search by IMDb ID if available, otherwise by title
            var searchQuery = !string.IsNullOrWhiteSpace(movie.ImdbId)
                ? movie.ImdbId
                : movie.Title;

            var (torrentResults, _) = await _torrentSearchService.SearchAsync(searchQuery, 1, 50, cancellationToken);

            // Filter results to match this specific movie
            var matchingTorrents = torrentResults.Where(t =>
                (!string.IsNullOrWhiteSpace(movie.ImdbId) && t.ImdbId == movie.ImdbId) ||
                (t.Title.Equals(movie.Title, StringComparison.OrdinalIgnoreCase) && t.Year == movie.Year)
            ).ToList();

            // Convert to TorrentOptionDto
            availableTorrents = matchingTorrents.Select(t => new TorrentOptionDto
            {
                Quality = t.Quality ?? "Unknown",
                MagnetLink = t.MagnetLink,
                TorrentUrl = t.TorrentUrl,
                Size = t.Size,
                Seeds = t.Seeds,
                Peers = t.Peers,
                Source = t.Source
            }).ToList();

            _logger.LogInformation("Found {Count} torrent options for movie {Title}", availableTorrents.Count, movie.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching torrents for movie {MovieId}", id);
            // Continue without torrents rather than failing the whole request
        }

        // Map to MovieDetailsDto
        return new MovieDetailsDto
        {
            Id = movie.Id,
            ImdbId = movie.ImdbId,
            Title = movie.Title,
            Year = movie.Year,
            Rating = movie.Rating,
            Genre = movie.Genre,
            Director = movie.Director,
            Cast = movie.Cast,
            Summary = movie.Summary,
            CoverImageUrl = movie.CoverImageUrl,
            Duration = movie.Duration,
            IsWatched = movie.LastWatchedAt.HasValue,
            LastWatchedAt = movie.LastWatchedAt,
            Torrents = availableTorrents
        };
    }

    private MovieDto MapToDto(Movie movie)
    {
        return new MovieDto
        {
            Id = movie.Id,
            ImdbId = movie.ImdbId,
            Title = movie.Title,
            Year = movie.Year,
            Rating = movie.Rating,
            Genre = movie.Genre,
            CoverImageUrl = movie.CoverImageUrl,
            IsWatched = movie.LastWatchedAt.HasValue
        };
    }
}
