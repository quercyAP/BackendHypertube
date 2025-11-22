namespace Hypertube.Application.Movies.DTOs;

public class TorrentSearchResultDto
{
    public string? ImdbId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public decimal? Rating { get; set; }
    public string? Genre { get; set; }
    public string? Summary { get; set; }
    public string? CoverImageUrl { get; set; }
    public int? Duration { get; set; }
    public string? Director { get; set; }
    public string? Cast { get; set; }
    
    // Torrent info
    public string MagnetLink { get; set; } = string.Empty;
    public string? TorrentUrl { get; set; }  // Direct .torrent file download URL
    public string? Quality { get; set; }  // 720p, 1080p, etc.
    public long? Size { get; set; }  // bytes
    public int? Seeds { get; set; }
    public int? Peers { get; set; }
    public string Source { get; set; } = string.Empty;  // YTS, EZTV, etc.
}
