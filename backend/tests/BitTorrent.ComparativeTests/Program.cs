using BitTorrent.ComparativeTests.ApiClients;
using BitTorrent.ComparativeTests.Tests;
using BitTorrent.ComparativeTests.Reporting;

namespace BitTorrent.ComparativeTests;

/// <summary>
/// Orchestrateur du test comparatif MonoTorrent vs Hypertube
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("  TEST COMPARATIF: MonoTorrent vs Hypertube BitTorrent Client  ");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        try
        {
            // Step 1: Rechercher un film populaire avec YTS
            Console.WriteLine("[ÉTAPE 1/4] Recherche d'un film populaire sur YTS...");
            Console.WriteLine();

            var ytsClient = new YtsApiClient();
            var torrent = await ytsClient.SearchPopularMovieAsync();

            if (torrent == null)
            {
                Console.WriteLine("✗ Aucun torrent trouvé sur YTS. Arrêt du test.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"✓ Torrent trouvé: {torrent.Title}");
            Console.WriteLine($"  Seeders: {torrent.Seeders}");
            Console.WriteLine($"  Taille: {FormatBytes(torrent.SizeBytes)}");
            Console.WriteLine($"  URL: {torrent.TorrentUrl}");
            Console.WriteLine();
            Console.WriteLine("─────────────────────────────────────────────────────────────");
            Console.WriteLine();

            // Step 2: Test avec MonoTorrent - TEMPORAIREMENT DÉSACTIVÉ
            Console.WriteLine("[ÉTAPE 2/4] Test MonoTorrent - SKIPPED (désactivé temporairement)");
            Console.WriteLine();

            // Créer des métriques vides pour MonoTorrent
            var monoMetrics = new Models.DownloadMetrics
            {
                Implementation = "MonoTorrent (SKIPPED)",
                MovieTitle = torrent.Title,
                StartTime = DateTime.Now,
                EndTime = DateTime.Now,
                Completed = false,
                ErrorMessage = "Test désactivé temporairement"
            };

            Console.WriteLine("─────────────────────────────────────────────────────────────");
            Console.WriteLine();

            // Step 3: Test avec Hypertube
            Console.WriteLine("[ÉTAPE 3/4] Test de téléchargement avec Hypertube...");
            Console.WriteLine();

            var hypertubeTest = new HypertubeDownloadTest();
            var downloadPath2 = Path.Combine(Path.GetTempPath(), $"hypertube_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(downloadPath2);

            var hypertubeMetrics = await hypertubeTest.DownloadMovieAsync(torrent.TorrentUrl, downloadPath2);

            Console.WriteLine("─────────────────────────────────────────────────────────────");
            Console.WriteLine();

            // Step 4: Générer le rapport comparatif
            Console.WriteLine("[ÉTAPE 4/4] Génération du rapport comparatif...");
            Console.WriteLine();

            var report = ComparisonReport.Generate(monoMetrics, hypertubeMetrics);
            Console.WriteLine(report);

            // Cleanup (optionnel)
            try
            {
                if (Directory.Exists(downloadPath2))
                {
                    Directory.Delete(downloadPath2, true);
                }
            }
            catch
            {
                Console.WriteLine("Note: Les fichiers téléchargés n'ont pas pu être supprimés automatiquement.");
                Console.WriteLine($"  - {downloadPath2}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine("                      ERREUR FATALE                            ");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine();
            Console.WriteLine($"Une erreur critique s'est produite: {ex.Message}");
            Console.WriteLine();
            Console.WriteLine("Stack trace:");
            Console.WriteLine(ex.StackTrace);
            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
        }
    }

    private static string FormatBytes(long bytes)
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
