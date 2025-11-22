namespace Hypertube.Application.Movies.DTOs;

public class TorrentDownloadProgressDto
{
    public Guid TorrentId { get; set; }
    public string MovieTitle { get; set; } = string.Empty;
    public double Progress { get; set; }
    public long DownloadSpeed { get; set; }
    public bool IsComplete { get; set; }
    public bool IsReadyForStreaming { get; set; }
    public string? FilePath { get; set; }
    public DateTime StartedAt { get; set; }
    public string Status { get; set; } = string.Empty;

    public bool IsConverting { get; set; }
    public double ConversionProgress { get; set; }

    public string? FileFormat { get; set; }
    public bool CanStreamNow { get; set; }
}
