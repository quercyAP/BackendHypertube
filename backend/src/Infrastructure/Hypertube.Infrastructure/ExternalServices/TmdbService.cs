using System.Text.Json;
using System.Text.Json.Serialization;
using Hypertube.Application.Movies.DTOs;
using Hypertube.Application.Movies.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hypertube.Infrastructure.ExternalServices;

public class TmdbService : IMovieMetadataService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TmdbService> _logger;
    private readonly string _apiKey;

    public TmdbService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<TmdbService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _apiKey = Environment.GetEnvironmentVariable("TMDB_API_KEY") ?? string.Empty;

        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("TMDB_API_KEY is not set in configuration");
        }

        _httpClient.BaseAddress = new Uri("https://api.themoviedb.org/3/");
    }

    public async Task<MovieMetadataDto?> GetMetadataAsync(string title, int? year = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            return null;
        }

        try
        {
            // Search for the movie
            var searchUrl = $"search/movie?api_key={_apiKey}&query={Uri.EscapeDataString(title)}";
            if (year.HasValue)
            {
                searchUrl += $"&year={year}";
            }

            var response = await _httpClient.GetAsync(searchUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TMDb search failed with status {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var searchResult = JsonSerializer.Deserialize<TmdbSearchResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var movie = searchResult?.Results?.FirstOrDefault();
            if (movie == null)
            {
                return null;
            }

            // Get full details for the movie (to get more info if needed, though search result has most)
            // For now, search result is enough for basic metadata, but let's get credits for director/cast
            var detailsUrl = $"movie/{movie.Id}?api_key={_apiKey}&append_to_response=credits";
            var detailsResponse = await _httpClient.GetAsync(detailsUrl, cancellationToken);
            
            if (detailsResponse.IsSuccessStatusCode)
            {
                var detailsJson = await detailsResponse.Content.ReadAsStringAsync(cancellationToken);
                var details = JsonSerializer.Deserialize<TmdbMovieDetails>(detailsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                if (details != null)
                {
                    return MapToDto(details);
                }
            }

            // Fallback to search result if details fetch fails
            return MapToDto(movie);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching metadata from TMDb for {Title}", title);
            return null;
        }
    }

    public async Task<MovieMetadataDto?> GetMetadataByImdbIdAsync(string imdbId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(imdbId))
        {
            return null;
        }

        try
        {
            // Use TMDB's "find" endpoint with external source (IMDb ID)
            var findUrl = $"find/{imdbId}?api_key={_apiKey}&external_source=imdb_id";

            var response = await _httpClient.GetAsync(findUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TMDb find by IMDb ID failed with status {Status} for {ImdbId}", response.StatusCode, imdbId);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var findResult = JsonSerializer.Deserialize<TmdbFindResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var movie = findResult?.MovieResults?.FirstOrDefault();
            if (movie == null)
            {
                _logger.LogInformation("No movie found for IMDb ID {ImdbId}", imdbId);
                return null;
            }

            // Get full details for credits
            var detailsUrl = $"movie/{movie.Id}?api_key={_apiKey}&append_to_response=credits";
            var detailsResponse = await _httpClient.GetAsync(detailsUrl, cancellationToken);

            if (detailsResponse.IsSuccessStatusCode)
            {
                var detailsJson = await detailsResponse.Content.ReadAsStringAsync(cancellationToken);
                var details = JsonSerializer.Deserialize<TmdbMovieDetails>(detailsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (details != null)
                {
                    _logger.LogInformation("Successfully enriched movie by IMDb ID {ImdbId}: {Title}", imdbId, details.Title);
                    return MapToDto(details);
                }
            }

            // Fallback to basic movie info
            return MapToDto(movie);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching metadata from TMDb by IMDb ID {ImdbId}", imdbId);
            return null;
        }
    }

    private MovieMetadataDto MapToDto(TmdbMovieDetails details)
    {
        var director = details.Credits?.Crew?.FirstOrDefault(c => c.Job == "Director")?.Name;
        var cast = details.Credits?.Cast?.Take(5).Select(c => c.Name).ToList() ?? new List<string>();

        return new MovieMetadataDto
        {
            Title = details.Title,
            Year = !string.IsNullOrEmpty(details.ReleaseDate) && DateTime.TryParse(details.ReleaseDate, out var date) ? date.Year : null,
            ImdbId = details.ImdbId,
            PosterUrl = !string.IsNullOrEmpty(details.PosterPath) ? $"https://image.tmdb.org/t/p/w500{details.PosterPath}" : null,
            Summary = details.Overview,
            Rating = (decimal)details.VoteAverage,
            Genre = details.Genres?.FirstOrDefault()?.Name,
            Director = director,
            Cast = cast,
            Runtime = details.Runtime
        };
    }

    private MovieMetadataDto MapToDto(TmdbMovieResult result)
    {
        return new MovieMetadataDto
        {
            Title = result.Title,
            Year = !string.IsNullOrEmpty(result.ReleaseDate) && DateTime.TryParse(result.ReleaseDate, out var date) ? date.Year : null,
            PosterUrl = !string.IsNullOrEmpty(result.PosterPath) ? $"https://image.tmdb.org/t/p/w500{result.PosterPath}" : null,
            Summary = result.Overview,
            Rating = (decimal)result.VoteAverage,
            // Genre IDs are available but need mapping, skipping for simple search result fallback
        };
    }

    // TMDb API Models
    private class TmdbSearchResponse
    {
        public List<TmdbMovieResult>? Results { get; set; }
    }

    private class TmdbMovieResult
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }
        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; set; }
        public string? Overview { get; set; }
        [JsonPropertyName("vote_average")]
        public double VoteAverage { get; set; }
    }

    private class TmdbMovieDetails : TmdbMovieResult
    {
        [JsonPropertyName("imdb_id")]
        public string? ImdbId { get; set; }
        public int? Runtime { get; set; }
        public List<TmdbGenre>? Genres { get; set; }
        public TmdbCredits? Credits { get; set; }
    }

    private class TmdbGenre
    {
        public string Name { get; set; } = string.Empty;
    }

    private class TmdbCredits
    {
        public List<TmdbCast>? Cast { get; set; }
        public List<TmdbCrew>? Crew { get; set; }
    }

    private class TmdbCast
    {
        public string Name { get; set; } = string.Empty;
    }

    private class TmdbCrew
    {
        public string Name { get; set; } = string.Empty;
        public string Job { get; set; } = string.Empty;
    }

    private class TmdbFindResponse
    {
        [JsonPropertyName("movie_results")]
        public List<TmdbMovieResult>? MovieResults { get; set; }
    }
}
