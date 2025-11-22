namespace BitTorrent.ComparativeTests.Models;

/// <summary>
/// Métriques de téléchargement pour comparaison
/// </summary>
public class DownloadMetrics
{
    public string Implementation { get; set; } = string.Empty; // "MonoTorrent" ou "Hypertube"
    public string MovieTitle { get; set; } = string.Empty;
    public int PeersFound { get; set; }
    public double AvgDownloadSpeedMBps { get; set; }
    public TimeSpan TotalTime { get; set; }
    public bool Completed { get; set; }
    public double PercentageCompleted { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
}
