using Hypertube.Application.Common.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hypertube.WebAPI.Controllers;

[ApiController]
[Route("api/mse")]
[Produces("application/json")]
public class MseController : ControllerBase
{
    private readonly IHlsPackagingService _hlsPackagingService;
    private readonly ILogger<MseController> _logger;

    public MseController(IHlsPackagingService hlsPackagingService, ILogger<MseController> logger)
    {
        _hlsPackagingService = hlsPackagingService;
        _logger = logger;
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
}
