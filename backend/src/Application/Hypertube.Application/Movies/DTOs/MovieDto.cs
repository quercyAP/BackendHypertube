namespace Hypertube.Application.Movies.DTOs;

public class MovieDto
{
    public Guid Id { get; set; }
    public string? ImdbId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public decimal? Rating { get; set; }
    public string? Genre { get; set; }
    public string? CoverImageUrl { get; set; }
    public bool IsWatched { get; set; }
}
