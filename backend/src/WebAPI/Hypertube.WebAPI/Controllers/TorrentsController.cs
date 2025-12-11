using Hypertube.Application.Movies.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hypertube.WebAPI.Controllers;

/// <summary>
/// Torrent download and streaming endpoints
/// </summary>
[ApiController]
[Route("api/torrents")]
[Produces("application/json")]
public class TorrentsController : ControllerBase
{
    private readonly ITorrentDownloadService _torrentDownloadService;
    private readonly ILogger<TorrentsController> _logger;

    public TorrentsController(
        ITorrentDownloadService torrentDownloadService,
        ILogger<TorrentsController> logger)
    {
        _torrentDownloadService = torrentDownloadService;
        _logger = logger;
    }

    /// <summary>
    /// Start downloading a torrent
    /// </summary>
    [HttpPost("download")]
    [Authorize]
    public async Task<IActionResult> StartDownload(
        [FromBody] StartDownloadRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received StartDownload request: TorrentUrl={TorrentUrl}, MovieTitle={MovieTitle}",
            request?.TorrentUrl ?? "null", request?.MovieTitle ?? "null");

        if (string.IsNullOrWhiteSpace(request.TorrentUrl))
        {
            return BadRequest(new { message = "TorrentUrl is required" });
        }

        if (string.IsNullOrWhiteSpace(request.MovieTitle))
        {
            return BadRequest(new { message = "MovieTitle is required" });
        }

        if (request.MovieId == Guid.Empty)
        {
            return BadRequest(new { message = "MovieId is required" });
        }

        try
        {
            var torrentId = await _torrentDownloadService.StartDownloadAsync(
                request.TorrentUrl,
                request.MovieTitle,
                request.MovieId,
                request.ImdbId,
                cancellationToken
            );

            return Ok(new { torrentId, message = "Download started successfully" });
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid torrent file for: {MovieTitle}", request.MovieTitle);
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start download for: {MovieTitle}", request.MovieTitle);
            return StatusCode(500, new { message = "Failed to start download", error = ex.Message });
        }
    }

    /// <summary>
    /// Get download progress for a torrent
    /// </summary>
    [HttpGet("{id}/progress")]
    [Authorize]
    public async Task<IActionResult> GetProgress(
        Guid id,
        CancellationToken cancellationToken)
    {
        var progress = await _torrentDownloadService.GetProgressAsync(id, cancellationToken);

        if (progress == null)
        {
            return NotFound(new { message = "Torrent not found" });
        }

        return Ok(progress);
    }

    /// <summary>
    /// Get all active downloads
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetAllDownloads(CancellationToken cancellationToken)
    {
        var downloads = await _torrentDownloadService.GetAllDownloadsAsync(cancellationToken);
        return Ok(new { downloads });
    }

    /// <summary>
    /// Stop a torrent download
    /// </summary>
    [HttpPost("{id}/stop")]
    [Authorize]
    public async Task<IActionResult> StopDownload(
        Guid id,
        CancellationToken cancellationToken)
    {
        await _torrentDownloadService.StopDownloadAsync(id, cancellationToken);
        return Ok(new { message = "Download stopped" });
    }

    /// <summary>
    /// Stream video file with HTTP Range support
    /// Serves converted file if available, returns 202 if conversion is in progress
    /// </summary>
    [HttpGet("{id}/stream")]
    [Authorize]
    public async Task<IActionResult> StreamVideo(
        Guid id,
        CancellationToken cancellationToken)
    {
        // Get progress to check conversion status
        var progress = await _torrentDownloadService.GetProgressAsync(id, cancellationToken);

        if (progress == null)
        {
            return NotFound(new { message = "Torrent not found" });
        }

        // If conversion is in progress, return 202 Accepted with progress
        if (progress.IsConverting)
        {
            return StatusCode(202, new
            {
                message = "Video is being converted",
                conversionProgress = progress.ConversionProgress
            });
        }

        var filePath = await _torrentDownloadService.GetVideoFilePathAsync(id, cancellationToken);

        if (filePath == null || !System.IO.File.Exists(filePath))
        {
            return NotFound(new { message = "Video file not found or not ready for streaming" });
        }

        var fileInfo = new FileInfo(filePath);
        _logger.LogInformation("Streaming video file with automatic range processing: {FilePath} (size: {Size} bytes)",
            filePath, fileInfo.Length);

        // Let ASP.NET Core handle Range headers and partial content automatically
        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return File(stream, "video/mp4", enableRangeProcessing: true);
    }
}

public class StartDownloadRequest
{
    public string TorrentUrl { get; set; } = string.Empty;
    public string MovieTitle { get; set; } = string.Empty;
    public Guid MovieId { get; set; }
    public string? ImdbId { get; set; }
}
