using Hypertube.Application.Common.Services;
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

    public MseController(IHlsPackagingService hlsPackagingService, ILogger<MseController> logger, IWebHostEnvironment environment)
    {
        _hlsPackagingService = hlsPackagingService;
        _logger = logger;
        _environment = environment;
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
    public IActionResult GetSubtitles(Guid torrentId)
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

            if (!Directory.Exists(torrentFolder))
            {
                return Ok(Array.Empty<object>());
            }

            var files = Directory.EnumerateFiles(torrentFolder, "sub_*.vtt")
                .OrderBy(path => path)
                .Select(path => new
                {
                    fileName = Path.GetFileName(path),
                    url = $"/hls/{torrentId:N}/{Path.GetFileName(path)}"
                })
                .ToArray();

            return Ok(files);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MSE] Failed to list subtitles for torrent {TorrentId}", torrentId);
            return StatusCode(500, new { message = "Failed to list subtitles", error = ex.Message });
        }
    }
}
