using System.Collections.Concurrent;
using System.Text.Json;
using FFMpegCore;
using FFMpegCore.Enums;
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

        LoadPersistedTorrents();
    }

    public async Task<Guid> StartDownloadAsync(string torrentUrl, string movieTitle, Guid movieId, string? imdbId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Downloading .torrent file from: {TorrentUrl}", torrentUrl);

            // Download .torrent file
            var torrentBytes = await _httpClient.GetByteArrayAsync(torrentUrl, cancellationToken);

            _logger.LogInformation("Parsing .torrent file ({Size} bytes)", torrentBytes.Length);

            // Parse .torrent file with error handling
            BitTorrent.Models.TorrentFile torrentFile;
            try
            {
                torrentFile = TorrentFileParser.Parse(torrentBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse .torrent file from URL: {TorrentUrl}", torrentUrl);
                throw new ArgumentException("The downloaded file is not a valid .torrent (invalid or missing 'info' section).", ex);
            }

            _logger.LogInformation("Torrent parsed successfully: InfoHash={InfoHash}, Name={Name}",
                torrentFile.InfoHashHex, torrentFile.Info.Name);

            var torrentId = Guid.NewGuid();
            var downloadPath = Path.Combine(_downloadDirectory, torrentFile.InfoHashHex);

            // Early validation: ensure there is exactly one video file with a known extension
            var videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm" };

            var files = torrentFile.Info.Files;

            // Build an explicit list of video files using the same extensions as GetLargestVideoFilePath
            List<BitTorrent.Models.TorrentFileInfo> videoFiles;
            if (files != null && files.Any())
            {
                videoFiles = files
                    .Where(f => videoExtensions.Any(ext => f.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }
            else
            {
                videoFiles = new List<BitTorrent.Models.TorrentFileInfo>();
            }

            // Single-file torrent: if no Files array, rely on Info.Name
            if (!videoFiles.Any() && (files == null || !files.Any()))
            {
                if (videoExtensions.Any(ext => torrentFile.Info.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                {
                    // Treat Info.Name as the single video file
                    // No need to store the relative path here; GetLargestVideoFilePath will resolve later.
                }
                else
                {
                    _logger.LogWarning("Torrent rejected: no video file with known extension (.mp4/.mkv/...) found in metadata: {Name}", torrentFile.Info.Name);

                    var rejectedInfo = new TorrentDownloadInfo
                    {
                        Id = torrentId,
                        MovieTitle = movieTitle,
                        MovieId = movieId,
                        ImdbId = imdbId,
                        TorrentFile = torrentFile,
                        DownloadManager = null,
                        StartedAt = DateTime.UtcNow,
                        DownloadPath = downloadPath,
                        CancellationTokenSource = null,
                        Status = "Incompatible",
                        ErrorMessage = "Torrent does not contain a direct video file (.mp4/.mkv/etc.). Archives or multi-file layouts are not supported for this project.",
                        IsConverting = false,
                        ConversionProgress = 0,
                        ConvertedFilePath = null,
                        PersistedFilePath = null
                    };

                    _activeDownloads[torrentId] = rejectedInfo;
                    return torrentId;
                }
            }

            // Multi-file torrent: require exactly one video file
            if (files != null && files.Any())
            {
                if (!videoFiles.Any())
                {
                    _logger.LogWarning("Torrent rejected: multi-file torrent without any video file with known extension: {Name}", torrentFile.Info.Name);

                    var rejectedInfo = new TorrentDownloadInfo
                    {
                        Id = torrentId,
                        MovieTitle = movieTitle,
                        TorrentFile = torrentFile,
                        DownloadManager = null,
                        StartedAt = DateTime.UtcNow,
                        DownloadPath = downloadPath,
                        CancellationTokenSource = null,
                        Status = "Incompatible",
                        ErrorMessage = "Multi-file torrent without any .mp4/.mkv/etc. video file is not supported.",
                        IsConverting = false,
                        ConversionProgress = 0,
                        ConvertedFilePath = null,
                        PersistedFilePath = null
                    };

                    _activeDownloads[torrentId] = rejectedInfo;
                    return torrentId;
                }

                if (videoFiles.Count > 1)
                {
                    _logger.LogWarning("Torrent rejected: multi-file torrent with more than one video candidate: {Name}", torrentFile.Info.Name);

                    var rejectedInfo = new TorrentDownloadInfo
                    {
                        Id = torrentId,
                        MovieTitle = movieTitle,
                        TorrentFile = torrentFile,
                        DownloadManager = null,
                        StartedAt = DateTime.UtcNow,
                        DownloadPath = downloadPath,
                        CancellationTokenSource = null,
                        Status = "Incompatible",
                        ErrorMessage = "Multi-video torrents (packs, discs, etc.) are not supported. Expected a single movie file.",
                        IsConverting = false,
                        ConversionProgress = 0,
                        ConvertedFilePath = null,
                        PersistedFilePath = null
                    };

                    _activeDownloads[torrentId] = rejectedInfo;
                    return torrentId;
                }
            }

            // Create download manager for a valid, single-video torrent
            var downloadManager = new TorrentDownloadManager(torrentFile, downloadPath);

            var cts = new CancellationTokenSource();
            var downloadInfo = new TorrentDownloadInfo
            {
                Id = torrentId,
                MovieTitle = movieTitle,
                MovieId = movieId,
                ImdbId = imdbId,
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

                        downloadInfo.CompletedAt = DateTime.UtcNow;

                        try
                        {
                            await SaveTorrentMetadataAsync(downloadInfo);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to persist torrent metadata for {TorrentId}", torrentId);
                        }
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

    public Task<(Guid MovieId, string? ImdbId)?> GetMovieInfoForTorrentAsync(Guid torrentId, CancellationToken cancellationToken = default)
    {
        if (_activeDownloads.TryGetValue(torrentId, out var info))
        {
            return Task.FromResult<(Guid MovieId, string? ImdbId)?>(new(info.MovieId, info.ImdbId));
        }

        return Task.FromResult<(Guid MovieId, string? ImdbId)?>(null);
    }

    public Task<TorrentDownloadProgressDto?> GetProgressAsync(Guid torrentId, CancellationToken cancellationToken = default)
    {
        if (!_activeDownloads.TryGetValue(torrentId, out var downloadInfo))
        {
            return Task.FromResult<TorrentDownloadProgressDto?>(null);
        }

        var filePath = downloadInfo.ConvertedFilePath ?? downloadInfo.PersistedFilePath ?? GetLargestVideoFilePath(downloadInfo);
        var fileFormat = GetFileFormat(filePath);

        if (downloadInfo.DownloadManager == null)
        {
            var reloadedProgress = new TorrentDownloadProgressDto
            {
                TorrentId = torrentId,
                MovieTitle = downloadInfo.MovieTitle,
                Progress = 100.0,
                DownloadSpeed = 0,
                IsComplete = true,
                IsReadyForStreaming = filePath != null,
                FilePath = filePath,
                StartedAt = downloadInfo.StartedAt,
                Status = downloadInfo.Status ?? "Completed",
                IsConverting = downloadInfo.IsConverting,
                ConversionProgress = downloadInfo.ConversionProgress,
                FileFormat = fileFormat,
                CanStreamNow = CanStreamFormat(fileFormat)
            };

            return Task.FromResult<TorrentDownloadProgressDto?>(reloadedProgress);
        }

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
            var filePath = downloadInfo.ConvertedFilePath ?? downloadInfo.PersistedFilePath ?? GetLargestVideoFilePath(downloadInfo);
            var fileFormat = GetFileFormat(filePath);

            if (downloadInfo.DownloadManager == null)
            {
                return new TorrentDownloadProgressDto
                {
                    TorrentId = downloadInfo.Id,
                    MovieTitle = downloadInfo.MovieTitle,
                    Progress = 100.0,
                    DownloadSpeed = 0,
                    IsComplete = true,
                    IsReadyForStreaming = filePath != null,
                    FilePath = filePath,
                    StartedAt = downloadInfo.StartedAt,
                    Status = downloadInfo.Status ?? "Completed",
                    IsConverting = downloadInfo.IsConverting,
                    ConversionProgress = downloadInfo.ConversionProgress,
                    FileFormat = fileFormat,
                    CanStreamNow = CanStreamFormat(fileFormat)
                };
            }

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
            downloadInfo.DownloadManager?.Dispose();
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

        var filePath = downloadInfo.ConvertedFilePath ?? downloadInfo.PersistedFilePath ?? GetLargestVideoFilePath(downloadInfo);
        return Task.FromResult<string?>(filePath);
    }

    public Task<bool> IsReadyForStreamingAsync(Guid torrentId, CancellationToken cancellationToken = default)
    {
        if (!_activeDownloads.TryGetValue(torrentId, out var downloadInfo))
        {
            return Task.FromResult(false);
        }

        if (downloadInfo.DownloadManager == null)
        {
            var filePath = downloadInfo.ConvertedFilePath ?? downloadInfo.PersistedFilePath ?? GetLargestVideoFilePath(downloadInfo);
            return Task.FromResult(filePath != null);
        }

        return Task.FromResult(downloadInfo.DownloadManager.Progress > 5.0);
    }

    public Task<string?> GetFinalVideoPathForMseAsync(Guid torrentId, CancellationToken cancellationToken = default)
    {
        if (!_activeDownloads.TryGetValue(torrentId, out var downloadInfo))
        {
            return Task.FromResult<string?>(null);
        }

        // If conversion is still running, we are not ready for MSE
        if (downloadInfo.IsConverting)
        {
            return Task.FromResult<string?>(null);
        }

        var filePath = downloadInfo.ConvertedFilePath ?? downloadInfo.PersistedFilePath ?? GetLargestVideoFilePath(downloadInfo);

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return Task.FromResult<string?>(null);
        }
        
        return Task.FromResult<string?>(filePath);
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
            _logger.LogWarning("No valid video file with a known extension found for processing: {TorrentId}", torrentId);
            downloadInfo.Status = "CodecUnknown";
            downloadInfo.ErrorMessage =
                "Unable to locate a valid video file in this torrent (no .mp4/.mkv/etc. found). Archives or extension-less data are not supported.";
            return;
        }

        using var scope = _serviceProvider.CreateScope();

        // Always detect codecs with ffprobe to avoid container-only decisions
        _logger.LogInformation("Detecting codecs for: {FilePath}", videoFilePath);
        var codecDetector = scope.ServiceProvider.GetRequiredService<IVideoCodecDetector>();
        var codecInfo = await codecDetector.DetectCodecsAsync(videoFilePath);

        if (codecInfo == null)
        {
            _logger.LogError("Failed to detect codecs for: {FilePath}", videoFilePath);
            downloadInfo.Status = "CodecUnknown";
            downloadInfo.ErrorMessage = "Unable to detect video format (ffprobe error)";
            return;
        }

        // Log detected internal subtitle tracks (for debugging / inspection)
        if (codecInfo.SubtitleTracks != null && codecInfo.SubtitleTracks.Count > 0)
        {
            foreach (var sub in codecInfo.SubtitleTracks)
            {
                _logger.LogInformation(
                    "[Subtitles] Detected internal subtitle track Index={Index}, Codec={Codec} for {FilePath}",
                    sub.Index, sub.Codec, videoFilePath);
            }
        }
        else
        {
            _logger.LogInformation("[Subtitles] No internal subtitle tracks detected for {FilePath}", videoFilePath);
        }

        // If codecs + container are already web-compatible, use the original file directly
        if (codecInfo.IsWebCompatible)
        {
            _logger.LogInformation(
                "Video is web-compatible according to codec detector: {FilePath} (Video={Video}, Audio={Audio}, Container={Container})",
                videoFilePath, codecInfo.VideoCodec, codecInfo.AudioCodec, codecInfo.ContainerFormat);

            downloadInfo.ConvertedFilePath = videoFilePath;
            if (string.IsNullOrEmpty(downloadInfo.Status))
            {
                downloadInfo.Status = "Seeding";
            }
            return;
        }

        // Special-case: HEVC + AAC — allow testing but warn about limited browser support
        if (codecInfo.VideoCodec == "hevc" && codecInfo.AudioCodec == "aac")
        {
            _logger.LogWarning(
                "HEVC+AAC detected – treating as CodecUnknown for development/testing: {FilePath} (Container={Container})",
                videoFilePath, codecInfo.ContainerFormat);

            downloadInfo.ConvertedFilePath = videoFilePath;
            downloadInfo.Status = "CodecUnknown";
            downloadInfo.ErrorMessage =
                "HEVC video – playback may or may not work in this browser. Officially only H.264+AAC is supported.";
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
                    _logger.LogDebug("Remux progress: {Percent:F2}%, File={File}", percent, videoFilePath);
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

            return;
        }

        // H.264 video with non-AAC audio: transcode audio only to AAC in MP4 container
        if (codecInfo.VideoCodec == "h264" && !string.IsNullOrEmpty(codecInfo.AudioCodec) && codecInfo.AudioCodec != "aac")
        {
            var outputPath = Path.Combine(Path.GetDirectoryName(videoFilePath)!,
                Path.GetFileNameWithoutExtension(videoFilePath) + "_audioaac.mp4");

            downloadInfo.IsConverting = true;
            downloadInfo.ConversionProgress = 0;
            downloadInfo.Status = "TranscodingAudio";

            _logger.LogInformation(
                "Starting audio-only transcode to AAC (H.264 video copy): {InputPath} -> {OutputPath} (Audio={AudioCodec})",
                videoFilePath, outputPath, codecInfo.AudioCodec);

            try
            {
                await FFMpegArguments
                    .FromFileInput(videoFilePath)
                    .OutputToFile(outputPath, overwrite: true, options => options
                        .WithCustomArgument("-c:v copy")
                        .WithAudioCodec(AudioCodec.Aac)
                        .WithCustomArgument("-b:a 160k")
                        .WithCustomArgument("-movflags faststart"))
                    .NotifyOnProgress(percent =>
                    {
                        downloadInfo.ConversionProgress = percent;
                        _logger.LogDebug("Audio transcode progress: {Percent:F2}%, File={File}", percent, videoFilePath);
                    }, TimeSpan.FromSeconds(1))
                    .ProcessAsynchronously();

                downloadInfo.ConvertedFilePath = outputPath;
                downloadInfo.IsConverting = false;
                downloadInfo.ConversionProgress = 100;
                downloadInfo.Status = "Seeding";

                _logger.LogInformation("Audio-only transcode completed: {OutputPath}", outputPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audio-only transcode failed: {InputPath}", videoFilePath);
                downloadInfo.IsConverting = false;
                downloadInfo.Status = "Error";
                downloadInfo.ErrorMessage = $"Audio transcoding failed: {ex.Message}";
            }

            return;
        }

        // Incompatible codecs - cannot stream
        _logger.LogWarning(
            "Video has incompatible codecs for streaming - Video: {VideoCodec}, Audio: {AudioCodec}",
            codecInfo.VideoCodec, codecInfo.AudioCodec);

        downloadInfo.Status = "Incompatible";
        downloadInfo.ErrorMessage = $"Video format not supported for streaming (Video: {codecInfo.VideoCodec}, Audio: {codecInfo.AudioCodec}). Only H.264+AAC videos can be streamed.";
    }

    private string? GetLargestVideoFilePath(TorrentDownloadInfo downloadInfo)
    {
        if (!string.IsNullOrEmpty(downloadInfo.PersistedFilePath) && File.Exists(downloadInfo.PersistedFilePath))
        {
            return downloadInfo.PersistedFilePath;
        }

        var videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm" };

        // 1) STRATÉGIE PRINCIPALE: scanner le dossier de téléchargement et retourner
        //    le plus gros fichier vidéo réellement présent sur disque.
        if (Directory.Exists(downloadInfo.DownloadPath))
        {
            // 1.a) D'abord, essayer les fichiers avec extension vidéo connue
            var videoFiles = Directory.EnumerateFiles(downloadInfo.DownloadPath, "*", SearchOption.AllDirectories)
                .Where(path => videoExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .Select(path => new FileInfo(path))
                .Where(fi => fi.Exists)
                .OrderByDescending(fi => fi.Length)
                .ToList();

            var largestVideoOnDisk = videoFiles.FirstOrDefault();
            if (largestVideoOnDisk != null)
            {
                return largestVideoOnDisk.FullName;
            }

            // 1.b) Si aucun fichier avec extension vidéo connue, prendre le plus gros
            //      fichier tout court (cas des torrents sans extension dans Info.Name).
            var allFiles = Directory.EnumerateFiles(downloadInfo.DownloadPath, "*", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .Where(fi => fi.Exists)
                .OrderByDescending(fi => fi.Length)
                .ToList();

            var largestAnyOnDisk = allFiles.FirstOrDefault();
            if (largestAnyOnDisk != null)
            {
                return largestAnyOnDisk.FullName;
            }
        }

        // 2) Fallback: logique multi-fichiers basée sur Info.Files (pour une future prise en
        //    charge plus fine du layout multi-file).
        var largestFile = downloadInfo.TorrentFile.Info.Files?
            .Where(f => videoExtensions.Any(ext => f.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(f => f.Length)
            .FirstOrDefault();

        if (largestFile != null)
        {
            var candidate = Path.Combine(downloadInfo.DownloadPath, largestFile.FullPath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // 3) Fallback: single file torrent avec extension explicite dans Name
        if (videoExtensions.Any(ext => downloadInfo.TorrentFile.Info.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
        {
            var candidate = Path.Combine(downloadInfo.DownloadPath, downloadInfo.TorrentFile.Info.Name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Aucun fichier avec extension vidéo connue n'a été trouvé sur disque.
        // On retourne null pour que l'appelant marque le torrent comme CodecUnknown.
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
        public Guid MovieId { get; set; }
        public string? ImdbId { get; set; }
        public required BitTorrent.Models.TorrentFile TorrentFile { get; set; }
        public TorrentDownloadManager? DownloadManager { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string DownloadPath { get; set; } = string.Empty;
        public string? Status { get; set; }
        public string? ErrorMessage { get; set; }
        public CancellationTokenSource? CancellationTokenSource { get; set; }

        // Video conversion fields
        public bool IsConverting { get; set; }
        public double ConversionProgress { get; set; }
        public string? ConvertedFilePath { get; set; }
        public string? PersistedFilePath { get; set; }
    }

    private class PersistedTorrentMetadata
    {
        public Guid TorrentId { get; set; }
        public string MovieTitle { get; set; } = string.Empty;
        public Guid MovieId { get; set; }
        public string? ImdbId { get; set; }
        public string InfoHash { get; set; } = string.Empty;
        public string DownloadPath { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public long? BytesDownloaded { get; set; }
        public string? OriginalTorrentUrl { get; set; }
    }

    private static byte[] ParseHexString(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return Array.Empty<byte>();
        }

        if (hex.Length % 2 != 0)
        {
            return Array.Empty<byte>();
        }

        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        return bytes;
    }

    private async Task SaveTorrentMetadataAsync(TorrentDownloadInfo downloadInfo)
    {
        var filePath = downloadInfo.ConvertedFilePath ?? downloadInfo.PersistedFilePath ?? GetLargestVideoFilePath(downloadInfo);
        if (filePath == null)
        {
            return;
        }

        var metadata = new PersistedTorrentMetadata
        {
            TorrentId = downloadInfo.Id,
            MovieTitle = downloadInfo.MovieTitle,
            MovieId = downloadInfo.MovieId,
            ImdbId = downloadInfo.ImdbId,
            InfoHash = downloadInfo.TorrentFile.InfoHashHex,
            DownloadPath = downloadInfo.DownloadPath,
            FilePath = filePath,
            Status = downloadInfo.Status ?? (downloadInfo.DownloadManager?.IsComplete == true ? "Completed" : "Downloading"),
            StartedAt = downloadInfo.StartedAt,
            CompletedAt = downloadInfo.CompletedAt
        };

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var jsonPath = Path.Combine(_downloadDirectory, $"{downloadInfo.Id}.json");
        await File.WriteAllTextAsync(jsonPath, json);
    }

    private void LoadPersistedTorrents()
    {
        try
        {
            if (!Directory.Exists(_downloadDirectory))
            {
                return;
            }

            var jsonFiles = Directory.EnumerateFiles(_downloadDirectory, "*.json", SearchOption.TopDirectoryOnly);

            foreach (var jsonFile in jsonFiles)
            {
                try
                {
                    var json = File.ReadAllText(jsonFile);
                    var metadata = JsonSerializer.Deserialize<PersistedTorrentMetadata>(json);
                    if (metadata == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(metadata.FilePath) || !File.Exists(metadata.FilePath))
                    {
                        continue;
                    }

                    if (!Guid.TryParse(metadata.TorrentId.ToString(), out var torrentId))
                    {
                        continue;
                    }

                    var torrentInfo = new TorrentDownloadInfo
                    {
                        Id = torrentId,
                        MovieTitle = metadata.MovieTitle,
                        MovieId = metadata.MovieId,
                        ImdbId = metadata.ImdbId,
                        TorrentFile = new BitTorrent.Models.TorrentFile
                        {
                            InfoHash = ParseHexString(metadata.InfoHash),
                            Info = new BitTorrent.Models.TorrentInfo()
                        },
                        DownloadManager = null,
                        StartedAt = metadata.StartedAt,
                        CompletedAt = metadata.CompletedAt,
                        DownloadPath = string.IsNullOrEmpty(metadata.DownloadPath)
                            ? Path.GetDirectoryName(metadata.FilePath) ?? _downloadDirectory
                            : metadata.DownloadPath,
                        Status = string.IsNullOrEmpty(metadata.Status) ? "Completed" : metadata.Status,
                        ErrorMessage = null,
                        CancellationTokenSource = null,
                        IsConverting = false,
                        ConversionProgress = 100,
                        ConvertedFilePath = null,
                        PersistedFilePath = metadata.FilePath
                    };

                    _activeDownloads[torrentId] = torrentInfo;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load persisted torrent metadata from {File}", jsonFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate persisted torrents in directory {Directory}", _downloadDirectory);
        }
    }
}
