using System.Collections.Concurrent;
using FFMpegCore;
using Hypertube.Application.Common.Services;
using Hypertube.Application.Movies.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Hypertube.Infrastructure.Services;

public class HlsPackagingService : IHlsPackagingService
{
    private readonly ITorrentDownloadService _torrentDownloadService;
    private readonly IVideoCodecDetector _codecDetector;
    private readonly ILogger<HlsPackagingService> _logger;
    private readonly string _hlsRootDirectory;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public HlsPackagingService(
        ITorrentDownloadService torrentDownloadService,
        IVideoCodecDetector codecDetector,
        IWebHostEnvironment environment,
        ILogger<HlsPackagingService> logger)
    {
        _torrentDownloadService = torrentDownloadService;
        _codecDetector = codecDetector;
        _logger = logger;

        var webRoot = environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
        {
            webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        }
        _hlsRootDirectory = Path.Combine(webRoot, "hls");
        Directory.CreateDirectory(_hlsRootDirectory);
    }

    public async Task<string?> GetOrCreatePlaylistAsync(Guid torrentId, CancellationToken cancellationToken = default)
    {
        var finalVideoPath = await _torrentDownloadService.GetFinalVideoPathForMseAsync(torrentId, cancellationToken);
        if (string.IsNullOrWhiteSpace(finalVideoPath) || !File.Exists(finalVideoPath))
        {
            _logger.LogWarning("[HLS] No MP4 available for torrent {TorrentId}", torrentId);
            return null;
        }

        var torrentFolder = Path.Combine(_hlsRootDirectory, torrentId.ToString("N"));
        var playlistPath = Path.Combine(torrentFolder, "index.m3u8");

        if (File.Exists(playlistPath))
        {
            return BuildRelativePlaylistUrl(torrentId);
        }

        var semaphore = _locks.GetOrAdd(torrentId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            if (File.Exists(playlistPath))
            {
                return BuildRelativePlaylistUrl(torrentId);
            }

            Directory.CreateDirectory(torrentFolder);
            CleanupDirectory(torrentFolder);

            var segmentPattern = Path.Combine(torrentFolder, "seg_%05d.ts");

            _logger.LogInformation("[HLS] Generating HLS playlist for torrent {TorrentId}. Input={Input} Output={Output}",
                torrentId, finalVideoPath, playlistPath);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await FFMpegArguments
                    .FromFileInput(finalVideoPath)
                    .OutputToFile(
                        playlistPath,
                        overwrite: true,
                        options => options
                            .WithCustomArgument("-map 0:v:0")
                            .WithCustomArgument("-map 0:a:0?")
                            .WithCustomArgument("-c:v copy")
                            .WithCustomArgument("-c:a copy")
                            .WithCustomArgument("-start_number 0")
                            .WithCustomArgument("-hls_time 4")
                            .WithCustomArgument("-hls_playlist_type vod")
                            .WithCustomArgument("-hls_flags independent_segments+program_date_time")
                            .WithCustomArgument($"-hls_segment_filename {segmentPattern}")
                            .WithCustomArgument("-f hls"))
                    .CancellableThrough(cancellationToken)
                    .ProcessAsynchronously();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HLS] ffmpeg packaging failed for torrent {TorrentId}", torrentId);
                CleanupDirectory(torrentFolder);
                throw;
            }
            finally
            {
                stopwatch.Stop();
            }

            if (!File.Exists(playlistPath))
            {
                _logger.LogError("[HLS] Playlist file missing after packaging for torrent {TorrentId} (looked at {PlaylistPath})",
                    torrentId, playlistPath);
                CleanupDirectory(torrentFolder);
                return null;
            }

            _logger.LogInformation("[HLS] Playlist ready for torrent {TorrentId} in {Elapsed}ms – stored at {PlaylistPath}",
                torrentId, stopwatch.ElapsedMilliseconds, playlistPath);

            // Best-effort extraction of internal text subtitle tracks to WebVTT files
            await ExtractSubtitlesAsync(finalVideoPath, torrentFolder, cancellationToken);

            return BuildRelativePlaylistUrl(torrentId);
        }
        finally
        {
            semaphore.Release();
            _locks.TryRemove(torrentId, out _);
        }
    }

    private static void CleanupDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // ignore cleanup errors; best effort
            }
        }
    }

    private static string BuildRelativePlaylistUrl(Guid torrentId)
    {
        return $"/hls/{torrentId:N}/index.m3u8";
    }

    private async Task ExtractSubtitlesAsync(string inputPath, string torrentFolder, CancellationToken cancellationToken)
    {
        try
        {
            var codecInfo = await _codecDetector.DetectCodecsAsync(inputPath, cancellationToken);
            if (codecInfo == null || codecInfo.SubtitleTracks == null || codecInfo.SubtitleTracks.Count == 0)
            {
                _logger.LogInformation("[HLS][Subs] No internal subtitle tracks found for {Input}", inputPath);
                return;
            }

            foreach (var track in codecInfo.SubtitleTracks)
            {
                // For now, only try to extract text-based subtitles. Image-based codecs (e.g. pgs) are skipped.
                var codec = track.Codec ?? string.Empty;
                if (string.IsNullOrEmpty(codec))
                {
                    continue;
                }

                // Common text subtitle codecs we can reasonably convert to WebVTT
                if (!(codec.Contains("subrip") || codec.Contains("ass") || codec.Contains("mov_text") || codec.Contains("webvtt")))
                {
                    _logger.LogInformation("[HLS][Subs] Skipping non-text subtitle track Index={Index}, Codec={Codec} for {Input}",
                        track.Index, track.Codec, inputPath);
                    continue;
                }

                var outputPath = Path.Combine(torrentFolder, $"sub_{track.Index}.vtt");

                _logger.LogInformation("[HLS][Subs] Extracting subtitle track Index={Index}, Codec={Codec} to {Output}",
                    track.Index, track.Codec, outputPath);

                try
                {
                    await FFMpegArguments
                        .FromFileInput(inputPath)
                        .OutputToFile(
                            outputPath,
                            overwrite: true,
                            options => options
                                // Use global stream index (0:{Index}) instead of relative subtitle index (0:s:{n})
                                .WithCustomArgument($"-map 0:{track.Index}")
                                .WithCustomArgument("-f webvtt"))
                        .CancellableThrough(cancellationToken)
                        .ProcessAsynchronously();

                    if (File.Exists(outputPath))
                    {
                        _logger.LogInformation("[HLS][Subs] Subtitle track Index={Index} extracted successfully to {Output}",
                            track.Index, outputPath);
                    }
                    else
                    {
                        _logger.LogWarning("[HLS][Subs] Expected subtitle file not found after extraction: {Output}", outputPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[HLS][Subs] Failed to extract subtitle track Index={Index} (Codec={Codec}) from {Input}",
                        track.Index, track.Codec, inputPath);
                }
            }
        }
        catch (Exception ex)
        {
            // Subtitle extraction is best-effort: never fail HLS packaging because of it.
            _logger.LogError(ex, "[HLS][Subs] Unexpected error while extracting subtitles for {Input}", inputPath);
        }
    }
}
