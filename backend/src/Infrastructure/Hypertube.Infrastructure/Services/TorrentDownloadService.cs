using System.Collections.Concurrent;
using Hypertube.Application.Common.Services;
using Hypertube.Application.Movies.DTOs;
using Hypertube.Application.Movies.Services;
using Hypertube.BitTorrent;
using Hypertube.BitTorrent.Parsers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hypertube.Infrastructure.Services;

public class TorrentDownloadService : ITorrentDownloadService
{
    private readonly ILogger<TorrentDownloadService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _downloadDirectory;
    private readonly ConcurrentDictionary<Guid, TorrentDownloadInfo> _activeDownloads = new();
    private readonly TorrentSeedingService _seedingService;
    private readonly IServiceProvider _serviceProvider;

    public TorrentDownloadService(
        ILogger<TorrentDownloadService> logger,
        HttpClient httpClient,
        string downloadDirectory,
        TorrentSeedingService seedingService,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _httpClient = httpClient;
        _downloadDirectory = downloadDirectory;
        _seedingService = seedingService;
        _serviceProvider = serviceProvider;

        // Create download directory if it doesn't exist
        Directory.CreateDirectory(_downloadDirectory);
    }

    public async Task<Guid> StartDownloadAsync(string torrentUrl, string movieTitle, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Downloading .torrent file from: {TorrentUrl}", torrentUrl);

            // Download .torrent file
            var torrentBytes = await _httpClient.GetByteArrayAsync(torrentUrl, cancellationToken);

            _logger.LogInformation("Parsing .torrent file ({Size} bytes)", torrentBytes.Length);

            // Parse .torrent file
            var torrentFile = TorrentFileParser.Parse(torrentBytes);

            _logger.LogInformation("Torrent parsed successfully: InfoHash={InfoHash}, Name={Name}",
                torrentFile.InfoHashHex, torrentFile.Info.Name);

            // Create download manager
            var downloadPath = Path.Combine(_downloadDirectory, torrentFile.InfoHashHex);
            var downloadManager = new TorrentDownloadManager(torrentFile, downloadPath);

            var torrentId = Guid.NewGuid();
            var cts = new CancellationTokenSource();
            var downloadInfo = new TorrentDownloadInfo
            {
                Id = torrentId,
                MovieTitle = movieTitle,
                TorrentFile = torrentFile,
                DownloadManager = downloadManager,
                StartedAt = DateTime.UtcNow,
                DownloadPath = downloadPath,
                CancellationTokenSource = cts
            };

            _activeDownloads[torrentId] = downloadInfo;

            _logger.LogInformation("Starting torrent download: {MovieTitle} (ID: {TorrentId})", movieTitle, torrentId);

            // Start download asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    await downloadManager.StartDownloadAsync(cts.Token);
                    _logger.LogInformation("Torrent download completed: {MovieTitle}", movieTitle);

                    // SEEDING SUPPORT: Register torrent for continuous seeding (subject requirement)
                    if (downloadManager.IsComplete)
                    {
                        _logger.LogInformation("[Seeding] Download complete at 100%, registering for seeding: {MovieTitle} (ID: {TorrentId})",
                            movieTitle, torrentId);
                        _seedingService.RegisterTorrentForSeeding(torrentId, downloadManager);
                        downloadInfo.Status = "Seeding"; // Update status to indicate seeding active

                        // Start conversion if needed
                        await StartConversionIfNeededAsync(torrentId);
                    }
                    else
                    {
                        _logger.LogWarning("[Seeding] Download finished but not complete ({Progress}%), not registering for seeding",
                            downloadManager.Progress);
                        downloadInfo.Status = "Incomplete";
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Torrent download cancelled: {MovieTitle}", movieTitle);
                    downloadInfo.Status = "Cancelled";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during torrent download: {MovieTitle}", movieTitle);
                    downloadInfo.Status = "Error";
                    downloadInfo.ErrorMessage = ex.Message;
                }
                finally
                {
                    cts.Dispose();
                }
            }, CancellationToken.None);

            return torrentId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start torrent download from URL: {TorrentUrl}", torrentUrl);
            throw;
        }
    }

    public Task<TorrentDownloadProgressDto?> GetProgressAsync(Guid torrentId, CancellationToken cancellationToken = default)
    {
        if (!_activeDownloads.TryGetValue(torrentId, out var downloadInfo))
        {
            return Task.FromResult<TorrentDownloadProgressDto?>(null);
        }

        var filePath = downloadInfo.ConvertedFilePath ?? GetLargestVideoFilePath(downloadInfo);
        var fileFormat = GetFileFormat(filePath);
        var canStreamNow = CanStreamFormat(fileFormat) && downloadInfo.DownloadManager.Progress > 5.0;

        var progress = new TorrentDownloadProgressDto
        {
            TorrentId = torrentId,
            MovieTitle = downloadInfo.MovieTitle,
            Progress = downloadInfo.DownloadManager.Progress,
            DownloadSpeed = downloadInfo.DownloadManager.DownloadSpeed,
            IsComplete = downloadInfo.DownloadManager.IsComplete,
            IsReadyForStreaming = downloadInfo.DownloadManager.Progress > 5.0,
            FilePath = filePath,
            StartedAt = downloadInfo.StartedAt,
            Status = downloadInfo.Status ?? (downloadInfo.DownloadManager.IsComplete ? "Completed" : "Downloading"),
            IsConverting = downloadInfo.IsConverting,
            ConversionProgress = downloadInfo.ConversionProgress,
            FileFormat = fileFormat,
            CanStreamNow = canStreamNow
        };

        return Task.FromResult<TorrentDownloadProgressDto?>(progress);
    }

    public Task<List<TorrentDownloadProgressDto>> GetAllDownloadsAsync(CancellationToken cancellationToken = default)
    {
        var progressList = _activeDownloads.Values.Select(downloadInfo =>
        {
            var filePath = downloadInfo.ConvertedFilePath ?? GetLargestVideoFilePath(downloadInfo);
            var fileFormat = GetFileFormat(filePath);
            var canStreamNow = CanStreamFormat(fileFormat) && downloadInfo.DownloadManager.Progress > 5.0;

            return new TorrentDownloadProgressDto
            {
                TorrentId = downloadInfo.Id,
                MovieTitle = downloadInfo.MovieTitle,
                Progress = downloadInfo.DownloadManager.Progress,
                DownloadSpeed = downloadInfo.DownloadManager.DownloadSpeed,
                IsComplete = downloadInfo.DownloadManager.IsComplete,
                IsReadyForStreaming = downloadInfo.DownloadManager.Progress > 5.0,
                FilePath = filePath,
                StartedAt = downloadInfo.StartedAt,
                Status = downloadInfo.Status ?? (downloadInfo.DownloadManager.IsComplete ? "Completed" : "Downloading"),
                IsConverting = downloadInfo.IsConverting,
                ConversionProgress = downloadInfo.ConversionProgress,
                FileFormat = fileFormat,
                CanStreamNow = canStreamNow
            };
        }).ToList();

        return Task.FromResult(progressList);
    }

    public async Task StopDownloadAsync(Guid torrentId, CancellationToken cancellationToken = default)
    {
        if (_activeDownloads.TryRemove(torrentId, out var downloadInfo))
        {
            downloadInfo.CancellationTokenSource?.Cancel();
            downloadInfo.DownloadManager.Dispose();
            _logger.LogInformation("Stopped torrent download: {MovieTitle}", downloadInfo.MovieTitle);
        }

        await Task.CompletedTask;
    }

    public Task<string?> GetVideoFilePathAsync(Guid torrentId, CancellationToken cancellationToken = default)
    {
        if (!_activeDownloads.TryGetValue(torrentId, out var downloadInfo))
        {
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult<string?>(GetLargestVideoFilePath(downloadInfo));
    }

    public Task<bool> IsReadyForStreamingAsync(Guid torrentId, CancellationToken cancellationToken = default)
    {
        if (!_activeDownloads.TryGetValue(torrentId, out var downloadInfo))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(downloadInfo.DownloadManager.Progress > 5.0);
    }

    private async Task StartConversionIfNeededAsync(Guid torrentId)
    {
        if (!_activeDownloads.TryGetValue(torrentId, out var downloadInfo))
        {
            return;
        }

        var videoFilePath = GetLargestVideoFilePath(downloadInfo);
        if (string.IsNullOrEmpty(videoFilePath) || !File.Exists(videoFilePath))
        {
            _logger.LogWarning("No video file found for processing: {TorrentId}", torrentId);
            return;
        }

        using var scope = _serviceProvider.CreateScope();

        // Check if file is already in web-compatible format (MP4/WebM)
        var fileFormat = GetFileFormat(videoFilePath);
        if (CanStreamFormat(fileFormat))
        {
            _logger.LogInformation("Video file is already web-compatible: {FilePath} (format: {Format})", videoFilePath, fileFormat);
            downloadInfo.ConvertedFilePath = videoFilePath;
            return;
        }

        // File needs processing (likely MKV) - detect codecs
        _logger.LogInformation("Detecting codecs for: {FilePath}", videoFilePath);
        var codecDetector = scope.ServiceProvider.GetRequiredService<IVideoCodecDetector>();
        var codecInfo = await codecDetector.DetectCodecsAsync(videoFilePath);

        if (codecInfo == null)
        {
            _logger.LogError("Failed to detect codecs for: {FilePath}", videoFilePath);
            downloadInfo.Status = "Error";
            downloadInfo.ErrorMessage = "Unable to detect video format";
            return;
        }

        // Check if file can be fast-remuxed (H.264 + AAC)
        if (codecInfo.VideoCodec == "h264" && codecInfo.AudioCodec == "aac")
        {
            // Fast remux (30 seconds) - just change container without transcoding
            var outputPath = Path.Combine(Path.GetDirectoryName(videoFilePath)!,
                Path.GetFileNameWithoutExtension(videoFilePath) + "_remuxed.mp4");

            downloadInfo.IsConverting = true;
            downloadInfo.ConversionProgress = 0;
            downloadInfo.Status = "Remuxing";

            _logger.LogInformation("Starting fast remux (H.264+AAC): {InputPath} -> {OutputPath}", videoFilePath, outputPath);

            try
            {
                var progress = new Progress<double>(percent =>
                {
                    downloadInfo.ConversionProgress = percent;
                    _logger.LogDebug("Remux progress: {Percent:F2}%", percent);
                });

                var remuxService = scope.ServiceProvider.GetRequiredService<IVideoRemuxService>();
                await remuxService.RemuxToMp4Async(videoFilePath, outputPath, progress);

                downloadInfo.ConvertedFilePath = outputPath;
                downloadInfo.IsConverting = false;
                downloadInfo.ConversionProgress = 100;
                downloadInfo.Status = "Seeding";

                _logger.LogInformation("Fast remux completed: {OutputPath}", outputPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fast remux failed: {InputPath}", videoFilePath);
                downloadInfo.IsConverting = false;
                downloadInfo.Status = "Error";
                downloadInfo.ErrorMessage = $"Processing failed: {ex.Message}";
            }
        }
        else
        {
            // Incompatible codecs - cannot stream
            _logger.LogWarning(
                "Video has incompatible codecs for streaming - Video: {VideoCodec}, Audio: {AudioCodec}",
                codecInfo.VideoCodec, codecInfo.AudioCodec);

            downloadInfo.Status = "Incompatible";
            downloadInfo.ErrorMessage = $"Video format not supported for streaming (Video: {codecInfo.VideoCodec}, Audio: {codecInfo.AudioCodec}). Only H.264+AAC videos can be streamed.";
        }
    }

    private string? GetLargestVideoFilePath(TorrentDownloadInfo downloadInfo)
    {
        var videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm" };

        var largestFile = downloadInfo.TorrentFile.Info.Files?
            .Where(f => videoExtensions.Any(ext => f.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(f => f.Length)
            .FirstOrDefault();

        if (largestFile != null)
        {
            return Path.Combine(downloadInfo.DownloadPath, largestFile.FullPath);
        }

        // Single file torrent
        if (videoExtensions.Any(ext => downloadInfo.TorrentFile.Info.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
        {
            return Path.Combine(downloadInfo.DownloadPath, downloadInfo.TorrentFile.Info.Name);
        }

        return null;
    }

    private string? GetFileFormat(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return null;
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension.TrimStart('.'); // Remove the leading dot
    }

    private bool CanStreamFormat(string? format)
    {
        if (string.IsNullOrEmpty(format))
        {
            return false;
        }

        // Only MP4 and WebM can be streamed directly without processing
        var webCompatibleFormats = new[] { "mp4", "webm" };
        return webCompatibleFormats.Contains(format.ToLowerInvariant());
    }

    private class TorrentDownloadInfo
    {
        public Guid Id { get; set; }
        public string MovieTitle { get; set; } = string.Empty;
        public required BitTorrent.Models.TorrentFile TorrentFile { get; set; }
        public required TorrentDownloadManager DownloadManager { get; set; }
        public DateTime StartedAt { get; set; }
        public string DownloadPath { get; set; } = string.Empty;
        public string? Status { get; set; }
        public string? ErrorMessage { get; set; }
        public CancellationTokenSource? CancellationTokenSource { get; set; }

        // Video conversion fields
        public bool IsConverting { get; set; }
        public double ConversionProgress { get; set; }
        public string? ConvertedFilePath { get; set; }
    }
}
