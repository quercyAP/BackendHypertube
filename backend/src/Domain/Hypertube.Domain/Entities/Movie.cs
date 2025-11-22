namespace Hypertube.Domain.Entities;

public class Movie
{
    public Guid Id { get; set; }
    public string? ImdbId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public decimal? Rating { get; set; }
    public string? Genre { get; set; }
    public string? Director { get; set; }
    public string? Cast { get; set; }
    public string? Summary { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? FilePath { get; set; }
    public long? FileSize { get; set; }
    public int? Duration { get; set; }
    public DateTime? DownloadedAt { get; set; }
    public DateTime? LastWatchedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public ICollection<Torrent> Torrents { get; set; } = new List<Torrent>();
}
