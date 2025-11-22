namespace Hypertube.Domain.Entities;

public class Torrent
{
    public Guid Id { get; set; }
    public Guid MovieId { get; set; }
    public string MagnetLink { get; set; } = string.Empty;
    public string Status { get; set; } = "pending"; // pending, downloading, completed, error
    public decimal Progress { get; set; }
    public long? DownloadSpeed { get; set; }
    public int? Peers { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Navigation properties
    public Movie Movie { get; set; } = null!;
}
