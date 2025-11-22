using Hypertube.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using BitTorrent.ComparativeTests.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BitTorrent.ComparativeTests.Tests;

/// <summary>
/// Test de téléchargement avec Hypertube (notre implémentation)
/// </summary>
public class HypertubeDownloadTest
{
    public async Task<DownloadMetrics> DownloadMovieAsync(string torrentUrl, string downloadPath)
    {
        var metrics = new DownloadMetrics
        {
            Implementation = "Hypertube",
            StartTime = DateTime.Now
        };

        Guid? torrentId = null;
        TorrentDownloadService? service = null;

        try
        {
            Console.WriteLine("[Hypertube] ====================================");
            Console.WriteLine("[Hypertube] Démarrage du téléchargement...");
            Console.WriteLine("[Hypertube] ====================================\n");

            // Créer le service Hypertube
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<TorrentDownloadService>();
            var seedingLogger = loggerFactory.CreateLogger<TorrentSeedingService>();
            var httpClient = new HttpClient();

            // Create seeding service for test (even though we won't use it for short tests)
            var seedingService = new TorrentSeedingService(seedingLogger);

            // Create a mock service provider for dependency injection (not used in tests)
            var serviceCollection = new ServiceCollection();
            var serviceProvider = serviceCollection.BuildServiceProvider();

            service = new TorrentDownloadService(logger, httpClient, downloadPath, seedingService, serviceProvider);

            // Démarrer le téléchargement
            torrentId = await service.StartDownloadAsync(torrentUrl, "Test Movie");

            Console.WriteLine($"[Hypertube] Torrent ID: {torrentId}");
            Console.WriteLine("[Hypertube] Connexion aux peers...\n");

            // Attendre un peu pour que les peers se connectent
            await Task.Delay(5000);

            var startTime = DateTime.Now;
            var lastProgress = 0.0;
            var speeds = new List<double>();
            var maxPeers = 0; // Note: Hypertube ne retourne pas le nombre de peers dans l'API actuelle

            // Boucle de monitoring
            while (true)
            {
                var progressDto = await service.GetProgressAsync(torrentId.Value);

                if (progressDto == null)
                {
                    Console.WriteLine("[Hypertube] ✗ ERREUR: Impossible de récupérer la progression");
                    metrics.ErrorMessage = "Unable to retrieve progress";
                    break;
                }

                var progress = progressDto.Progress;
                var speedBps = progressDto.DownloadSpeed;

                metrics.MovieTitle = progressDto.MovieTitle;

                // Collecter les vitesses
                if (speedBps > 0) speeds.Add(speedBps / 1024.0 / 1024.0); // Convert to MB/s

                // Afficher le progrès
                if (Math.Abs(progress - lastProgress) > 0.5 || progress >= 100)
                {
                    var elapsed = DateTime.Now - startTime;
                    Console.WriteLine($"[Hypertube] {progress:F1}% | {speedBps / 1024.0:F0} KB/s | {elapsed:hh\\:mm\\:ss}");
                    lastProgress = progress;
                }

                if (progressDto.IsComplete || progress >= 100)
                {
                    metrics.Completed = true;
                    metrics.PercentageCompleted = 100;
                    break;
                }

                await Task.Delay(2000); // Update every 2 seconds

                // Timeout après 10 minutes pour le test
                if ((DateTime.Now - startTime).TotalMinutes > 10)
                {
                    Console.WriteLine($"\n[Hypertube] Timeout après 10 minutes");
                    metrics.PercentageCompleted = progress;
                    break;
                }
            }

            metrics.EndTime = DateTime.Now;
            metrics.TotalTime = metrics.EndTime.Value - metrics.StartTime;
            metrics.PeersFound = maxPeers; // Note: Non disponible dans l'API actuelle
            metrics.AvgDownloadSpeedMBps = speeds.Count > 0 ? speeds.Average() : 0;

            Console.WriteLine($"\n[Hypertube] ====================================");
            Console.WriteLine($"[Hypertube] Test terminé!");
            Console.WriteLine($"[Hypertube] ====================================\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[Hypertube] ✗ ERREUR: {ex.Message}");
            Console.WriteLine($"[Hypertube] Stack trace: {ex.StackTrace}");
            metrics.ErrorMessage = ex.Message;
            metrics.EndTime = DateTime.Now;
            metrics.TotalTime = metrics.EndTime.Value - metrics.StartTime;
        }
        finally
        {
            // Cleanup
            if (torrentId.HasValue && service != null)
            {
                try
                {
                    await service.StopDownloadAsync(torrentId.Value);
                }
                catch
                {
                    // Ignore cleanup errors
                }
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
