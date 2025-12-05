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
    private readonly ILogger<HlsPackagingService> _logger;
    private readonly string _hlsRootDirectory;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public HlsPackagingService(
        ITorrentDownloadService torrentDownloadService,
        IWebHostEnvironment environment,
        ILogger<HlsPackagingService> logger)
    {
        _torrentDownloadService = torrentDownloadService;
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
}
