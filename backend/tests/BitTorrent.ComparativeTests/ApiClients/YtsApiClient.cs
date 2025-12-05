using System.Text.Json;
using BitTorrent.ComparativeTests.Models;

namespace BitTorrent.ComparativeTests.ApiClients;

/// <summary>
/// Client pour l'API YTS (https://yts.lt)
/// </summary>
public class YtsApiClient
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://yts.lt/api/v2";

    public YtsApiClient()
    {
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Recherche un film populaire avec un bon nombre de seeders
    /// </summary>
    public async Task<TorrentSearchResult?> SearchPopularMovieAsync()
    {
        try
        {
            // Chercher les films les plus populaires, triés par seeders, qualité 720p minimum
            var url = $"{BaseUrl}/list_movies.json?limit=10&quality=720p&sort_by=seeds&order_by=desc&minimum_rating=6";

            Console.WriteLine($"[YTS] Recherche de films populaires...");
            Console.WriteLine($"[YTS] URL: {url}");

            var response = await _httpClient.GetStringAsync(url);
            var jsonDoc = JsonDocument.Parse(response);

            var movies = jsonDoc.RootElement.GetProperty("data").GetProperty("movies");

            if (movies.ValueKind == JsonValueKind.Undefined || movies.GetArrayLength() == 0)
            {
                Console.WriteLine("[YTS] Aucun film trouvé");
                return null;
            }

            // Prendre le premier film avec des torrents disponibles
            foreach (var movie in movies.EnumerateArray())
            {
                var title = movie.GetProperty("title").GetString() ?? "Unknown";
                var torrents = movie.GetProperty("torrents");

                foreach (var torrent in torrents.EnumerateArray())
                {
                    var quality = torrent.GetProperty("quality").GetString() ?? "720p";
                    var seeders = torrent.GetProperty("seeds").GetInt32();
                    var torrentUrl = torrent.GetProperty("url").GetString();
                    var sizeBytes = ParseSize(torrent.GetProperty("size").GetString() ?? "0");

                    // Chercher un torrent avec au moins 10 seeders
                    if (seeders >= 10 && !string.IsNullOrEmpty(torrentUrl))
                    {
                        Console.WriteLine($"[YTS] ✓ Film trouvé: {title} ({quality})");
                        Console.WriteLine($"[YTS]   Seeders: {seeders}");
                        Console.WriteLine($"[YTS]   Taille: {FormatBytes(sizeBytes)}");

                        return new TorrentSearchResult
                        {
                            Title = $"{title} ({quality})",
                            TorrentUrl = torrentUrl,
                            SizeBytes = sizeBytes,
                            Seeders = seeders,
                            Quality = quality
                        };
                    }
                }
            }

            Console.WriteLine("[YTS] Aucun torrent avec suffisamment de seeders");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[YTS] ✗ Erreur: {ex.Message}");
            return null;
        }
    }

    private long ParseSize(string sizeStr)
    {
        // Parse "698.75 MB" ou "1.4 GB" en bytes
        var parts = sizeStr.Split(' ');
        if (parts.Length != 2 || !double.TryParse(parts[0], out var value))
            return 0;

        var multiplier = parts[1].ToUpper() switch
        {
            "KB" => 1024L,
            "MB" => 1024L * 1024,
            "GB" => 1024L * 1024 * 1024,
            _ => 1L
        };

        return (long)(value * multiplier);
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
