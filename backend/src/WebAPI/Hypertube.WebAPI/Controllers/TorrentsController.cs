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

        try
        {
            var torrentId = await _torrentDownloadService.StartDownloadAsync(
                request.TorrentUrl,
                request.MovieTitle,
                cancellationToken
            );

            return Ok(new { torrentId, message = "Download started successfully" });
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
        var fileSize = fileInfo.Length;

        // Parse Range header
        var rangeHeader = Request.Headers["Range"].ToString();
        if (string.IsNullOrEmpty(rangeHeader))
        {
            // No range requested, return entire file
            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, "video/mp4", enableRangeProcessing: true);
        }

        // Parse Range: bytes=start-end
        var range = rangeHeader.Replace("bytes=", "").Split('-');
        var start = long.Parse(range[0]);
        var end = range.Length > 1 && !string.IsNullOrEmpty(range[1])
            ? long.Parse(range[1])
            : fileSize - 1;

        // Validate range
        if (start >= fileSize || end >= fileSize)
        {
            return StatusCode(416, new { message = "Requested range not satisfiable" });
        }

        var contentLength = end - start + 1;

        // Open file stream and seek to start position
        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        fileStream.Seek(start, SeekOrigin.Begin);

        // Set response headers for partial content
        Response.StatusCode = 206; // Partial Content
        Response.Headers.Add("Accept-Ranges", "bytes");
        Response.Headers.Add("Content-Range", $"bytes {start}-{end}/{fileSize}");
        Response.Headers.Add("Content-Length", contentLength.ToString());
        Response.ContentType = "video/mp4";

        _logger.LogInformation("Streaming video range: {Start}-{End}/{FileSize}", start, end, fileSize);

        // Return limited stream
        return File(fileStream, "video/mp4");
    }
}

public class StartDownloadRequest
{
    public string TorrentUrl { get; set; } = string.Empty;
    public string MovieTitle { get; set; } = string.Empty;
}
