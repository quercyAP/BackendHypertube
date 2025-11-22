namespace Hypertube.Application.Movies.DTOs;

public class TorrentOptionDto
{
    public string Quality { get; set; } = string.Empty;
    public string MagnetLink { get; set; } = string.Empty;
    public string? TorrentUrl { get; set; }
    public long? Size { get; set; }
    public int? Seeds { get; set; }
    public int? Peers { get; set; }
    public string Source { get; set; } = string.Empty;
}
