namespace Hypertube.Application.Movies.DTOs;

public class MovieMetadataDto
{
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string? ImdbId { get; set; }
    public string? PosterUrl { get; set; }
    public string? Summary { get; set; }
    public decimal? Rating { get; set; }
    public string? Genre { get; set; }
    public string? Director { get; set; }
    public List<string> Cast { get; set; } = new();
    public int? Runtime { get; set; }
}
