using Hypertube.Application.Movies.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hypertube.WebAPI.Controllers;

/// <summary>
/// Movies library endpoints
/// </summary>
[ApiController]
[Route("api/movies")]
[Produces("application/json")]
public class MoviesController : ControllerBase
{
    private readonly IMovieService _movieService;
    private readonly ITorrentSearchService _torrentSearchService;
    private readonly ILogger<MoviesController> _logger;

    public MoviesController(
        IMovieService movieService,
        ITorrentSearchService torrentSearchService,
        ILogger<MoviesController> logger)
    {
        _movieService = movieService;
        _torrentSearchService = torrentSearchService;
        _logger = logger;
    }

    /// <summary>
    /// Get popular movies (paginated)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMovies(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? genre = null,
        [FromQuery] int? year = null,
        [FromQuery] decimal? minRating = null,
        [FromQuery] string sortBy = "rating",
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        // If filters provided, use filtered search
        if (!string.IsNullOrWhiteSpace(genre) || year.HasValue || minRating.HasValue)
        {
            var (movies, totalCount) = await _movieService.GetByFiltersAsync(
                genre, year, minRating, sortBy, page, pageSize, cancellationToken
            );

            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            return Ok(new
            {
                movies,
                totalPages,
                currentPage = page,
                totalCount,
                hasMore = page < totalPages
            });
        }

        // Otherwise get popular movies
        var (popularMovies, count) = await _movieService.GetPopularAsync(page, pageSize, cancellationToken);

        var popularTotalPages = (int)Math.Ceiling(count / (double)pageSize);
        return Ok(new
        {
            movies = popularMovies,
            totalPages = popularTotalPages,
            currentPage = page,
            totalCount = count,
            hasMore = page < popularTotalPages
        });
    }

    /// <summary>
    /// Search movies by title
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> SearchMovies(
        [FromQuery] string q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest(new { message = "Search query is required" });
        }

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var (movies, totalCount) = await _movieService.SearchAsync(q, page, pageSize, cancellationToken);

        var searchTotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        return Ok(new
        {
            movies,
            totalPages = searchTotalPages,
            currentPage = page,
            totalCount,
            hasMore = page < searchTotalPages
        });
    }

    /// <summary>
    /// Search torrents from YTS and PirateBay (stores results in database)
    /// </summary>
    [HttpGet("search-torrents")]
    public async Task<IActionResult> SearchTorrents(
        [FromQuery] string query,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { message = "Search query is required" });
        }

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var (movies, totalCount) = await _movieService.SearchAsync(query, page, pageSize, cancellationToken);

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        return Ok(new
        {
            movies,
            totalPages,
            currentPage = page,
            totalCount,
            hasMore = page < totalPages
        });
    }

    /// <summary>
    /// Get movie details by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetMovieDetails(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var movie = await _movieService.GetMovieDetailsAsync(id, cancellationToken);

        if (movie == null)
        {
            return NotFound(new { message = "Movie not found" });
        }

        return Ok(movie);
    }
}
