using Hypertube.BitTorrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hypertube.Infrastructure.Services;

/// <summary>
/// BackgroundService pour le seeding continu des torrents téléchargés
/// Implémente le requirement du sujet: "while the app is running and movie is cached, the torrent should be SEEDING"
/// </summary>
/// <remarks>
/// Ce service:
/// - Garde les TorrentDownloadManager actifs après téléchargement complet
/// - Permet aux peers de continuer à télécharger depuis nous
/// - Implémente le tit-for-tat reciprocity (upload = download)
/// - Se termine proprement au shutdown de l'app
/// </remarks>
public class TorrentSeedingService : BackgroundService
{
    private readonly ILogger<TorrentSeedingService> _logger;
    private readonly Dictionary<Guid, TorrentDownloadManager> _seedingTorrents = new();
    private readonly object _seedingLock = new object();

    public TorrentSeedingService(ILogger<TorrentSeedingService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Register a torrent for continuous seeding after download completion
    /// </summary>
    /// <param name="torrentId">Unique identifier for the torrent</param>
    /// <param name="manager">TorrentDownloadManager instance (must be kept alive)</param>
    public void RegisterTorrentForSeeding(Guid torrentId, TorrentDownloadManager manager)
    {
        lock (_seedingLock)
        {
            if (_seedingTorrents.ContainsKey(torrentId))
            {
                _logger.LogWarning("[Seeding] Torrent {TorrentId} already registered for seeding", torrentId);
                return;
            }

            _seedingTorrents[torrentId] = manager;
            _logger.LogInformation("[Seeding] Torrent {TorrentId} registered for seeding (total: {Count})",
                torrentId, _seedingTorrents.Count);
        }
    }

    /// <summary>
    /// Unregister and dispose a torrent (e.g., when movie deleted after 30 days)
    /// </summary>
    /// <param name="torrentId">Unique identifier for the torrent</param>
    public void UnregisterTorrent(Guid torrentId)
    {
        lock (_seedingLock)
        {
            if (_seedingTorrents.TryGetValue(torrentId, out var manager))
            {
                _logger.LogInformation("[Seeding] Unregistering torrent {TorrentId}", torrentId);

                // Dispose manager to release resources (file handles, network connections)
                manager.Dispose();

                _seedingTorrents.Remove(torrentId);
                _logger.LogInformation("[Seeding] Torrent {TorrentId} unregistered and disposed (remaining: {Count})",
                    torrentId, _seedingTorrents.Count);
            }
            else
            {
                _logger.LogWarning("[Seeding] Torrent {TorrentId} not found in seeding registry", torrentId);
            }
        }
    }

    /// <summary>
    /// Background task execution: Log seeding status periodically
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for graceful shutdown</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Seeding] Background seeding service started");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Wait 60 seconds between status logs
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);

                // Log current seeding status
                int seedingCount;
                lock (_seedingLock)
                {
                    seedingCount = _seedingTorrents.Count;
                }

                if (seedingCount > 0)
                {
                    _logger.LogInformation("[Seeding] Currently seeding {Count} torrents", seedingCount);

                    // Optional: Log detailed statistics for each torrent
                    lock (_seedingLock)
                    {
                        foreach (var (torrentId, manager) in _seedingTorrents)
                        {
                            _logger.LogDebug("[Seeding]   - Torrent {TorrentId}: Uploaded {Uploaded} bytes",
                                torrentId, manager.BytesUploaded);
                        }
                    }
                }
                else
                {
                    _logger.LogDebug("[Seeding] No torrents currently seeding");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
            _logger.LogInformation("[Seeding] Background seeding service stopping (cancellation requested)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Seeding] Background seeding service failed with exception");
        }
        finally
        {
            // Graceful shutdown: Dispose all managers
            _logger.LogInformation("[Seeding] Disposing all seeding torrents on shutdown");
            lock (_seedingLock)
            {
                foreach (var (torrentId, manager) in _seedingTorrents)
                {
                    try
                    {
                        manager.Dispose();
                        _logger.LogDebug("[Seeding] Disposed torrent {TorrentId}", torrentId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[Seeding] Error disposing torrent {TorrentId}", torrentId);
                    }
                }
                _seedingTorrents.Clear();
            }
            _logger.LogInformation("[Seeding] Background seeding service stopped");
        }
    }
}
