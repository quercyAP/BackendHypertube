namespace Hypertube.Application.Movies.DTOs;

public class MovieDetailsDto
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
    public int? Duration { get; set; }
    public bool IsWatched { get; set; }
    public DateTime? LastWatchedAt { get; set; }

    public List<TorrentOptionDto> Torrents { get; set; } = new();
}
