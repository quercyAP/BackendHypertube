using System.Text;
using BitTorrent.ComparativeTests.Models;

namespace BitTorrent.ComparativeTests.Reporting;

/// <summary>
/// Génère un rapport de comparaison entre les deux implémentations
/// </summary>
public static class ComparisonReport
{
    /// <summary>
    /// Génère un rapport de comparaison formaté
    /// </summary>
    public static string Generate(DownloadMetrics monoTorrent, DownloadMetrics hypertube)
    {
        var sb = new StringBuilder();

        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine("         RAPPORT COMPARATIF: MonoTorrent vs Hypertube         ");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine();

        // Film téléchargé
        sb.AppendLine($"Film testé: {monoTorrent.MovieTitle}");
        sb.AppendLine();

        // Table de comparaison
        sb.AppendLine("┌─────────────────────────────┬──────────────────┬──────────────────┐");
        sb.AppendLine("│ Métrique                    │ MonoTorrent      │ Hypertube        │");
        sb.AppendLine("├─────────────────────────────┼──────────────────┼──────────────────┤");

        // Completion
        var monoComplete = monoTorrent.Completed ? "✓" : "✗";
        var hyperComplete = hypertube.Completed ? "✓" : "✗";
        sb.AppendLine($"│ Téléchargement complet      │ {monoComplete,-16} │ {hyperComplete,-16} │");

        // Percentage
        sb.AppendLine($"│ Pourcentage complété        │ {monoTorrent.PercentageCompleted,14:F1}% │ {hypertube.PercentageCompleted,14:F1}% │");

        // Total time
        var monoTime = FormatTimeSpan(monoTorrent.TotalTime);
        var hyperTime = FormatTimeSpan(hypertube.TotalTime);
        sb.AppendLine($"│ Temps total                 │ {monoTime,-16} │ {hyperTime,-16} │");

        // Peers found
        sb.AppendLine($"│ Peers trouvés               │ {monoTorrent.PeersFound,-16} │ {hypertube.PeersFound,-16} │");

        // Average speed
        var monoSpeed = $"{monoTorrent.AvgDownloadSpeedMBps:F2} MB/s";
        var hyperSpeed = $"{hypertube.AvgDownloadSpeedMBps:F2} MB/s";
        sb.AppendLine($"│ Vitesse moyenne             │ {monoSpeed,-16} │ {hyperSpeed,-16} │");

        // Errors
        var monoError = string.IsNullOrEmpty(monoTorrent.ErrorMessage) ? "Aucune" : "Erreur";
        var hyperError = string.IsNullOrEmpty(hypertube.ErrorMessage) ? "Aucune" : "Erreur";
        sb.AppendLine($"│ Erreurs                     │ {monoError,-16} │ {hyperError,-16} │");

        sb.AppendLine("└─────────────────────────────┴──────────────────┴──────────────────┘");
        sb.AppendLine();

        // Erreurs détaillées
        if (!string.IsNullOrEmpty(monoTorrent.ErrorMessage))
        {
            sb.AppendLine($"Erreur MonoTorrent: {monoTorrent.ErrorMessage}");
        }
        if (!string.IsNullOrEmpty(hypertube.ErrorMessage))
        {
            sb.AppendLine($"Erreur Hypertube: {hypertube.ErrorMessage}");
        }
        if (!string.IsNullOrEmpty(monoTorrent.ErrorMessage) || !string.IsNullOrEmpty(hypertube.ErrorMessage))
        {
            sb.AppendLine();
        }

        // Analyse comparative
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine("                         ANALYSE                               ");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine();

        // Déterminer le gagnant
        if (monoTorrent.Completed && !hypertube.Completed)
        {
            sb.AppendLine("✓ MonoTorrent a réussi à télécharger le film complet");
            sb.AppendLine("✗ Hypertube n'a pas terminé le téléchargement");
            sb.AppendLine();
            sb.AppendLine("RÉSULTAT: MonoTorrent est plus fiable pour ce test");
        }
        else if (hypertube.Completed && !monoTorrent.Completed)
        {
            sb.AppendLine("✓ Hypertube a réussi à télécharger le film complet");
            sb.AppendLine("✗ MonoTorrent n'a pas terminé le téléchargement");
            sb.AppendLine();
            sb.AppendLine("RÉSULTAT: Hypertube est plus fiable pour ce test");
        }
        else if (monoTorrent.Completed && hypertube.Completed)
        {
            sb.AppendLine("✓ Les deux implémentations ont téléchargé le film complet");
            sb.AppendLine();

            // Comparer les vitesses
            if (monoTorrent.AvgDownloadSpeedMBps > hypertube.AvgDownloadSpeedMBps * 1.1)
            {
                var diff = ((monoTorrent.AvgDownloadSpeedMBps / hypertube.AvgDownloadSpeedMBps - 1) * 100);
                sb.AppendLine($"MonoTorrent est plus rapide ({diff:F0}% plus rapide)");
                sb.AppendLine("RÉSULTAT: MonoTorrent a de meilleures performances");
            }
            else if (hypertube.AvgDownloadSpeedMBps > monoTorrent.AvgDownloadSpeedMBps * 1.1)
            {
                var diff = ((hypertube.AvgDownloadSpeedMBps / monoTorrent.AvgDownloadSpeedMBps - 1) * 100);
                sb.AppendLine($"Hypertube est plus rapide ({diff:F0}% plus rapide)");
                sb.AppendLine("RÉSULTAT: Hypertube a de meilleures performances");
            }
            else
            {
                sb.AppendLine("Les deux implémentations ont des performances similaires");
                sb.AppendLine("RÉSULTAT: Performances équivalentes");
            }
        }
        else
        {
            sb.AppendLine("✗ Aucune des deux implémentations n'a terminé le téléchargement");
            sb.AppendLine();
            sb.AppendLine("RÉSULTAT: Les deux implémentations ont échoué");
        }

        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>
    /// Formate un TimeSpan de manière lisible
    /// </summary>
    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s";
        else if (ts.TotalMinutes >= 1)
            return $"{ts.Minutes}m {ts.Seconds}s";
        else
            return $"{ts.Seconds}s";
    }
}
