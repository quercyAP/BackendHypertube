using Hypertube.Application.Common.Services;
using Hypertube.Application.Movies.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hypertube.WebAPI.Controllers;

/// <summary>
/// Subtitle download and serving endpoints
/// </summary>
[ApiController]
[Route("api/movies")]
[Produces("application/json")]
public class SubtitlesController : ControllerBase
{
    private readonly IMovieService _movieService;
    private readonly ISubtitleService _subtitleService;
    private readonly ILogger<SubtitlesController> _logger;

    public SubtitlesController(
        IMovieService movieService,
        ISubtitleService subtitleService,
        ILogger<SubtitlesController> logger)
    {
        _movieService = movieService;
        _subtitleService = subtitleService;
        _logger = logger;
    }

    /// <summary>
    /// Get available subtitle languages for a movie
    /// </summary>
    /// <param name="id">Movie ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of available subtitle languages</returns>
    [HttpGet("{id}/subtitles/available")]
    [Authorize]
    public async Task<IActionResult> GetAvailableSubtitles(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get movie from database
            var movie = await _movieService.GetMovieDetailsAsync(id, cancellationToken);

            if (movie == null)
            {
                return NotFound(new { message = "Movie not found" });
            }

            // Check if movie has IMDb ID
            if (string.IsNullOrEmpty(movie.ImdbId))
            {
                _logger.LogWarning("Movie {MovieId} does not have IMDb ID", id);
                return Ok(new { languages = new string[0], message = "No IMDb ID available for this movie" });
            }

            // Search for available subtitles
            var languages = new[] { "en", "fr", "es", "de", "it", "pt", "ru", "zh", "ja", "ko" };
            var subtitles = await _subtitleService.SearchSubtitlesAsync(
                movie.ImdbId,
                languages,
                cancellationToken);

            // Return available language codes and names
            var availableLanguages = subtitles.Select(s => new
            {
                code = s.Language,
                name = s.LanguageName
            }).ToList();

            _logger.LogInformation("Found {Count} subtitle languages for movie {MovieId}",
                availableLanguages.Count, id);

            return Ok(new { languages = availableLanguages });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching available subtitles for movie {MovieId}", id);
            return StatusCode(500, new { message = "Failed to fetch available subtitles", error = ex.Message });
        }
    }

    /// <summary>
    /// Download and serve subtitle file for a movie
    /// </summary>
    /// <param name="id">Movie ID</param>
    /// <param name="lang">Language code (e.g., "en", "fr")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>WebVTT subtitle file</returns>
    [HttpGet("{id}/subtitles")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSubtitle(
        Guid id,
        [FromQuery] string lang,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(lang))
        {
            return BadRequest(new { message = "Language parameter is required" });
        }

        try
        {
            // Check if subtitle is cached
            var cachedPath = _subtitleService.GetCachedSubtitlePath(id, lang);
            if (cachedPath != null && System.IO.File.Exists(cachedPath))
            {
                _logger.LogInformation("Serving cached subtitle: {Path}", cachedPath);
                var cachedContent = await System.IO.File.ReadAllBytesAsync(cachedPath, cancellationToken);
                return File(cachedContent, "text/vtt", $"{lang}.vtt");
            }

            // Get movie from database
            var movie = await _movieService.GetMovieDetailsAsync(id, cancellationToken);

            if (movie == null)
            {
                return NotFound(new { message = "Movie not found" });
            }

            // Check if movie has IMDb ID
            if (string.IsNullOrEmpty(movie.ImdbId))
            {
                _logger.LogWarning("Movie {MovieId} does not have IMDb ID", id);
                return NotFound(new { message = "No IMDb ID available for this movie" });
            }

            // Search for subtitles in requested language (may return multiple candidates)
            var subtitles = await _subtitleService.SearchSubtitlesAsync(
                movie.ImdbId,
                new[] { lang },
                cancellationToken);

            if (subtitles == null || !subtitles.Any())
            {
                _logger.LogInformation("No subtitles found for movie {MovieId} in language {Language}",
                    id, lang);
                return NotFound(new { message = $"No subtitles found for language: {lang}" });
            }

            // Create output directory for this movie
            var outputDir = Path.Combine(
                Environment.GetEnvironmentVariable("DOWNLOAD_DIRECTORY")
                    ?? Path.Combine(Directory.GetCurrentDirectory(), "downloads"),
                "subtitles",
                id.ToString());

            string? srtPath = null;

            // Try each candidate until one successfully downloads
            foreach (var candidate in subtitles)
            {
                try
                {
                    srtPath = await _subtitleService.DownloadSubtitleAsync(
                        candidate.Id,
                        lang,
                        outputDir,
                        cancellationToken);

                    if (!string.IsNullOrEmpty(srtPath))
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to download subtitle candidate {SubtitleId} for movie {MovieId} in language {Language}",
                        candidate.Id, id, lang);
                }
            }

            if (string.IsNullOrEmpty(srtPath))
            {
                _logger.LogInformation("All subtitle candidates failed to download for movie {MovieId} in language {Language}", id, lang);
                return NotFound(new { message = "Subtitle not available from external provider" });
            }

            // Convert to WebVTT
            var vttPath = await _subtitleService.ConvertSrtToWebVttAsync(srtPath, cancellationToken);

            // Serve the WebVTT file
            var vttContent = await System.IO.File.ReadAllBytesAsync(vttPath, cancellationToken);

            _logger.LogInformation("Serving subtitle for movie {MovieId}, language {Language}", id, lang);

            return File(vttContent, "text/vtt", $"{lang}.vtt");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving subtitle for movie {MovieId}, language {Language}", id, lang);

            // Best-effort: if the underlying provider cannot deliver the subtitle (e.g. /download 404),
            // expose this as a 404 so the client can fall back gracefully instead of a hard 500.
            return NotFound(new { message = "Subtitle not available from external provider", error = ex.Message });
        }
    }
}
