using MonoTorrent;
using MonoTorrent.Client;
using BitTorrent.ComparativeTests.Models;

namespace BitTorrent.ComparativeTests.Tests;

/// <summary>
/// Test de téléchargement avec MonoTorrent (référence)
/// </summary>
public class MonoTorrentDownloadTest
{
    public async Task<DownloadMetrics> DownloadMovieAsync(string torrentUrl, string downloadPath)
    {
        var metrics = new DownloadMetrics
        {
            Implementation = "MonoTorrent",
            StartTime = DateTime.Now
        };

        ClientEngine? engine = null;
        TorrentManager? manager = null;

        try
        {
            Console.WriteLine("[MonoTorrent] ====================================");
            Console.WriteLine("[MonoTorrent] Démarrage du téléchargement...");
            Console.WriteLine("[MonoTorrent] ====================================\n");

            // Télécharger le fichier .torrent
            using var httpClient = new HttpClient();
            var torrentData = await httpClient.GetByteArrayAsync(torrentUrl);
            var torrent = await Torrent.LoadAsync(torrentData);

            metrics.MovieTitle = torrent.Name ?? "Unknown";
            Console.WriteLine($"[MonoTorrent] Film: {metrics.MovieTitle}");
            Console.WriteLine($"[MonoTorrent] Taille: {FormatBytes(torrent.Size)}\n");

            // Créer le moteur MonoTorrent
            var engineSettingsBuilder = new EngineSettingsBuilder
            {
                AllowPortForwarding = false
            };
            var engineSettings = engineSettingsBuilder.ToSettings();
            engine = new ClientEngine(engineSettings);

            // Créer le manager de torrent
            var torrentSettings = new TorrentSettings();
            manager = await engine.AddAsync(torrent, downloadPath, torrentSettings);

            // Démarrer le téléchargement
            await manager.StartAsync();

            Console.WriteLine("[MonoTorrent] Connexion aux peers...\n");

            // Attendre un peu pour que les peers se connectent
            await Task.Delay(5000);

            var startTime = DateTime.Now;
            var lastProgress = 0.0;
            var speeds = new List<double>();
            var maxPeers = 0;

            // Boucle de monitoring
            while (manager.State != TorrentState.Stopped && manager.State != TorrentState.Seeding)
            {
                var progress = manager.Progress;
                var speedBps = manager.Monitor.DownloadRate;
                var peers = manager.OpenConnections;

                if (peers > maxPeers) maxPeers = peers;
                if (speedBps > 0) speeds.Add(speedBps / 1024.0 / 1024.0); // Convert to MB/s

                // Afficher le progrès
                if (Math.Abs(progress - lastProgress) > 0.5 || progress >= 100)
                {
                    var elapsed = DateTime.Now - startTime;
                    Console.WriteLine($"[MonoTorrent] {progress:F1}% | {speedBps / 1024.0:F0} KB/s | {peers} peers | {elapsed:hh\\:mm\\:ss}");
                    lastProgress = progress;
                }

                if (progress >= 100)
                {
                    metrics.Completed = true;
                    metrics.PercentageCompleted = 100;
                    break;
                }

                await Task.Delay(2000); // Update every 2 seconds

                // Timeout après 10 minutes pour le test
                if ((DateTime.Now - startTime).TotalMinutes > 10)
                {
                    Console.WriteLine($"\n[MonoTorrent] Timeout après 10 minutes");
                    metrics.PercentageCompleted = progress;
                    break;
                }
            }

            // Capture final progress if not already set
            if (!metrics.Completed && metrics.PercentageCompleted == 0)
            {
                metrics.PercentageCompleted = manager.Progress;
            }

            metrics.EndTime = DateTime.Now;
            metrics.TotalTime = metrics.EndTime.Value - metrics.StartTime;
            metrics.PeersFound = maxPeers;
            metrics.AvgDownloadSpeedMBps = speeds.Count > 0 ? speeds.Average() : 0;

            Console.WriteLine($"\n[MonoTorrent] ====================================");
            Console.WriteLine($"[MonoTorrent] Test terminé!");
            Console.WriteLine($"[MonoTorrent] ====================================\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[MonoTorrent] ✗ ERREUR: {ex.Message}");
            metrics.ErrorMessage = ex.Message;
            metrics.EndTime = DateTime.Now;
            metrics.TotalTime = metrics.EndTime.Value - metrics.StartTime;
        }
        finally
        {
            // Cleanup
            if (manager != null)
            {
                await manager.StopAsync();
            }
            if (engine != null)
            {
                await engine.StopAllAsync();
                engine.Dispose();
            }
        }

        return metrics;
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
