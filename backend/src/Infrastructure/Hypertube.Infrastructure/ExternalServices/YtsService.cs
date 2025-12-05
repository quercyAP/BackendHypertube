using System.Text.Json;
using Hypertube.Application.Movies.DTOs;
using Microsoft.Extensions.Logging;

namespace Hypertube.Infrastructure.ExternalServices;

public class YtsService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<YtsService> _logger;
    private const string BaseUrl = "https://yts.lt/api/v2";

    public YtsService(HttpClient httpClient, ILogger<YtsService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<TorrentSearchResultDto>> SearchAsync(string query, int page = 1, int limit = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{BaseUrl}/list_movies.json?query_term={Uri.EscapeDataString(query)}&page={page}&limit={limit}",
                cancellationToken
            );

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("YTS API returned status {StatusCode}", response.StatusCode);
                return new List<TorrentSearchResultDto>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var ytsResponse = JsonSerializer.Deserialize<YtsApiResponse>(json, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            return MapYtsMoviesToDto(ytsResponse?.Data?.Movies);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching YTS for query: {Query}", query);
            return new List<TorrentSearchResultDto>();
        }
    }

    public async Task<List<TorrentSearchResultDto>> GetPopularAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{BaseUrl}/list_movies.json?sort_by=rating&page={page}&limit={limit}",
                cancellationToken
            );

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("YTS API returned status {StatusCode}", response.StatusCode);
                return new List<TorrentSearchResultDto>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var ytsResponse = JsonSerializer.Deserialize<YtsApiResponse>(json, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            return MapYtsMoviesToDto(ytsResponse?.Data?.Movies);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting popular movies from YTS");
            return new List<TorrentSearchResultDto>();
        }
    }

    private List<TorrentSearchResultDto> MapYtsMoviesToDto(List<YtsMovie>? movies)
    {
        if (movies == null || !movies.Any())
            return new List<TorrentSearchResultDto>();

        var results = new List<TorrentSearchResultDto>();

        foreach (var movie in movies)
        {
            if (movie.Torrents == null || !movie.Torrents.Any())
                continue;

            // Create one result per torrent quality
            foreach (var torrent in movie.Torrents)
            {
                results.Add(new TorrentSearchResultDto
                {
                    ImdbId = movie.ImdbCode,
                    Title = movie.Title,
                    Year = movie.Year,
                    Rating = movie.Rating,
                    Genre = movie.Genres != null && movie.Genres.Any() ? string.Join(", ", movie.Genres) : null,
                    Summary = movie.Summary,
                    CoverImageUrl = movie.MediumCoverImage,
                    Duration = movie.Runtime,
                    MagnetLink = BuildMagnetLink(torrent.Hash, movie.Title, torrent.Quality),
                    TorrentUrl = torrent.Url,  // Direct .torrent file URL from YTS
                    Quality = torrent.Quality,
                    Size = torrent.SizeBytes,
                    Seeds = torrent.Seeds,
                    Peers = torrent.Peers,
                    Source = "YTS"
                });
            }
        }

        return results;
    }

    private string BuildMagnetLink(string hash, string movieTitle, string quality)
    {
        var displayName = $"{movieTitle} [{quality}] [YTS]";
        return $"magnet:?xt=urn:btih:{hash}&dn={Uri.EscapeDataString(displayName)}" +
               "&tr=udp://tracker.opentrackr.org:1337/announce" +
               "&tr=udp://open.stealth.si:80/announce" +
               "&tr=udp://explodie.org:6969/announce" +
               "&tr=udp://tracker.torrent.eu.org:451/announce" +
               "&tr=udp://open.demonoid.ch:6969/announce" +
               "&tr=udp://tracker.qu.ax:6969/announce" +
               "&tr=udp://tracker.plx.im:6969/announce" +
               "&tr=udp://wepzone.net:6969/announce";
    }

    // YTS API Response Models
    private class YtsApiResponse
    {
        public string Status { get; set; } = string.Empty;
        public YtsData? Data { get; set; }
    }

    private class YtsData
    {
        public List<YtsMovie>? Movies { get; set; }
    }

    private class YtsMovie
    {
        public string Title { get; set; } = string.Empty;
        public int? Year { get; set; }
        public decimal? Rating { get; set; }
        public int? Runtime { get; set; }
        public List<string>? Genres { get; set; }
        public string? Summary { get; set; }
        public string? MediumCoverImage { get; set; }
        public string? ImdbCode { get; set; }
        public List<YtsTorrent>? Torrents { get; set; }
    }

    private class YtsTorrent
    {
        public string Url { get; set; } = string.Empty;  // Direct .torrent download URL
        public string Hash { get; set; } = string.Empty;
        public string Quality { get; set; } = string.Empty;
        public int? Seeds { get; set; }
        public int? Peers { get; set; }
        public long? SizeBytes { get; set; }
    }
}
