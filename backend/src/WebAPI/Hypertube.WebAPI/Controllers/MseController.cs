using Hypertube.Application.Common.Services;
using Hypertube.Application.Movies.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using System.IO;

namespace Hypertube.WebAPI.Controllers;

[ApiController]
[Route("api/mse")]
[Produces("application/json")]
public class MseController : ControllerBase
{
    private readonly IHlsPackagingService _hlsPackagingService;
    private readonly ILogger<MseController> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly ITorrentDownloadService _torrentDownloadService;
    private readonly ISubtitleService _subtitleService;

    public MseController(
        IHlsPackagingService hlsPackagingService,
        ILogger<MseController> logger,
        IWebHostEnvironment environment,
        ITorrentDownloadService torrentDownloadService,
        ISubtitleService subtitleService)
    {
        _hlsPackagingService = hlsPackagingService;
        _logger = logger;
        _environment = environment;
        _torrentDownloadService = torrentDownloadService;
        _subtitleService = subtitleService;
    }

    /// <summary>
    /// Returns (or generates) the HLS playlist URL for a torrent MP4 ready for MSE playback.
    /// </summary>
    [HttpGet("hls/{torrentId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetHlsPlaylist(Guid torrentId, CancellationToken cancellationToken)
    {
        try
        {
            var playlistUrl = await _hlsPackagingService.GetOrCreatePlaylistAsync(torrentId, cancellationToken);
            if (playlistUrl == null)
            {
                return NotFound(new { message = "No MP4 available for this torrent or conversion still in progress." });
            }

            return Ok(new { playlistUrl });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new { message = "Request cancelled" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MSE] Failed to build HLS playlist for torrent {TorrentId}", torrentId);
            return StatusCode(500, new { message = "Failed to generate HLS playlist", error = ex.Message });
        }
    }

    [HttpGet("subtitles/{torrentId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetSubtitles(Guid torrentId, CancellationToken cancellationToken)
    {
        try
        {
            var webRoot = _environment.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRoot))
            {
                webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            }

            var hlsRoot = Path.Combine(webRoot, "hls");
            var torrentFolder = Path.Combine(hlsRoot, torrentId.ToString("N"));

            var results = new List<object>();

            if (Directory.Exists(torrentFolder))
            {
                var internalFiles = Directory.EnumerateFiles(torrentFolder, "sub_*.vtt")
                    .OrderBy(path => path)
                    .Select(path =>
                    {
                        var fileName = Path.GetFileName(path);
                        var url = $"/hls/{torrentId:N}/{fileName}";
                        var meta = ParseSubtitleMetadataFromFileName(fileName);

                        return new
                        {
                            fileName,
                            url,
                            language = meta.Language,
                            title = meta.Title,
                            isForced = meta.IsForced,
                            source = "internal"
                        };
                    })
                    .ToArray();

                results.AddRange(internalFiles);
            }

            // Best-effort: try to add an external English subtitle track using movie metadata
            try
            {
                var movieInfo = await _torrentDownloadService.GetMovieInfoForTorrentAsync(torrentId, cancellationToken);
                if (movieInfo.HasValue && movieInfo.Value.MovieId != Guid.Empty && !string.IsNullOrWhiteSpace(movieInfo.Value.ImdbId))
                {
                    var (movieId, imdbId) = movieInfo.Value;

                    // Always try to provide English external subtitles if available
                    var subs = await _subtitleService.SearchSubtitlesAsync(imdbId!, new[] { "en" }, cancellationToken);
                    var enSub = subs.FirstOrDefault(s => string.Equals(s.Language, "en", StringComparison.OrdinalIgnoreCase));

                    if (enSub != null)
                    {
                        var externalUrl = $"/api/movies/{movieId}/subtitles?lang=en";
                        results.Add(new
                        {
                            fileName = "external_en.vtt",
                            url = externalUrl,
                            language = "en",
                            title = "English (external)",
                            isForced = false,
                            source = "external"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[MSE] Failed to resolve external subtitles for torrent {TorrentId}", torrentId);
            }

            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MSE] Failed to list subtitles for torrent {TorrentId}", torrentId);
            return StatusCode(500, new { message = "Failed to list subtitles", error = ex.Message });
        }
    }

    private static (string? Language, string? Title, bool IsForced) ParseSubtitleMetadataFromFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return (null, null, false);
        }

        try
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(nameWithoutExt))
            {
                return (null, null, false);
            }

            // Expected pattern: sub_{index}_{lang}[_forced]
            var parts = nameWithoutExt.Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                return (null, null, false);
            }

            var language = parts[2];
            var isForced = parts.Any(p => string.Equals(p, "forced", StringComparison.OrdinalIgnoreCase));

            return (language, null, isForced);
        }
        catch
        {
            return (null, null, false);
        }
    }
}
