using System.Text.Json;
using System.Text.Json.Serialization;
using Hypertube.Application.Movies.DTOs;
using Microsoft.Extensions.Logging;

namespace Hypertube.Infrastructure.ExternalServices;

public class PirateBayService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PirateBayService> _logger;
    // Using a public TPB API proxy
    private const string BaseUrl = "https://apibay.org";

    public PirateBayService(HttpClient httpClient, ILogger<PirateBayService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<List<TorrentSearchResultDto>> SearchAsync(string query, int page = 1, int limit = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            // TPB API: /q.php?q=query&cat=201 (201 = Movies category)
            var response = await _httpClient.GetAsync(
                $"/q.php?q={Uri.EscapeDataString(query)}&cat=201",
                cancellationToken
            );

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PirateBay API returned status {StatusCode}", response.StatusCode);
                return new List<TorrentSearchResultDto>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var torrents = JsonSerializer.Deserialize<List<TpbTorrent>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var allResults = MapTpbTorrentsToDto(torrents, query);
            return allResults.Skip((page - 1) * limit).Take(limit).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching PirateBay for query: {Query}", query);
            return new List<TorrentSearchResultDto>();
        }
    }

    public async Task<List<TorrentSearchResultDto>> GetPopularAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get popular movies (cat 201)
            var response = await _httpClient.GetAsync(
                $"/precompiled/data_top100_201.json",
                cancellationToken
            );

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PirateBay API returned status {StatusCode}", response.StatusCode);
                return new List<TorrentSearchResultDto>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var torrents = JsonSerializer.Deserialize<List<TpbTorrent>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var allResults = MapTpbTorrentsToDto(torrents, "popular");
            return allResults.Skip((page - 1) * limit).Take(limit).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting popular movies from PirateBay");
            return new List<TorrentSearchResultDto>();
        }
    }

    private List<TorrentSearchResultDto> MapTpbTorrentsToDto(List<TpbTorrent>? torrents, string searchQuery)
    {
        if (torrents == null || !torrents.Any())
            return new List<TorrentSearchResultDto>();

        var results = new List<TorrentSearchResultDto>();

        foreach (var torrent in torrents)
        {
            // Skip if no seeders (JsonConverter already parsed strings to int)
            if (torrent.Seeders <= 0)
                continue;

            // Skip multi-movie collections and packs
            if (torrent.NumFiles > 10)
                continue;

            var lowerName = torrent.Name.ToLowerInvariant();

            // Skip collections, packs, and series
            if (lowerName.Contains("pack") ||
                lowerName.Contains("collection") ||
                lowerName.Contains("complete") ||
                lowerName.Contains("season") ||
                lowerName.Contains("trilogy") ||
                System.Text.RegularExpressions.Regex.IsMatch(lowerName, @"\d+\s*x\s*") || // "3x Movies"
                System.Text.RegularExpressions.Regex.IsMatch(lowerName, @"\d+\s*(movie|film)s")) // "101 movies"
                continue;

            // Try to extract year from title (format: "Movie Title (2023)")
            var title = torrent.Name;
            int? year = ExtractYearFromTitle(title);

            // Extract quality if present in title
            string? quality = ExtractQualityFromTitle(title);

            // Clean IMDb ID if present (remove "tt" prefix for consistency)
            string? imdbId = !string.IsNullOrWhiteSpace(torrent.ImdbId) ? torrent.ImdbId : null;

            results.Add(new TorrentSearchResultDto
            {
                ImdbId = imdbId,
                Title = CleanTitle(title),
                Year = year,
                Summary = null, // TPB doesn't provide summaries - will be enriched by TMDB
                MagnetLink = BuildMagnetLink(torrent.InfoHash, torrent.Name),
                TorrentUrl = $"https://itorrents.net/torrent/{torrent.InfoHash}.torrent",  // Construct .torrent URL
                Quality = quality,
                Size = torrent.Size > 0 ? torrent.Size : null,
                Seeds = torrent.Seeders > 0 ? torrent.Seeders : null,
                Peers = torrent.Leechers > 0 ? torrent.Leechers : null,
                Source = "PirateBay"
            });
        }

        return results;
    }

    private string BuildMagnetLink(string infoHash, string name)
    {
        return $"magnet:?xt=urn:btih:{infoHash}&dn={Uri.EscapeDataString(name)}" +
               "&tr=udp://tracker.opentrackr.org:1337/announce" +
               "&tr=udp://open.stealth.si:80/announce" +
               "&tr=udp://explodie.org:6969/announce" +
               "&tr=udp://tracker.torrent.eu.org:451/announce" +
               "&tr=udp://open.demonoid.ch:6969/announce" +
               "&tr=udp://tracker.qu.ax:6969/announce" +
               "&tr=udp://tracker.plx.im:6969/announce" +
               "&tr=udp://wepzone.net:6969/announce";
    }

    private int? ExtractYearFromTitle(string title)
    {
        // Try (YYYY) format first (e.g., "Movie Title (2023)")
        var yearMatch = System.Text.RegularExpressions.Regex.Match(title, @"\((\d{4})\)");
        if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out var year1))
        {
            return year1;
        }

        // Try .YYYY. or YYYY format (common in torrents: "La.Haine.1995.REMASTERED")
        yearMatch = System.Text.RegularExpressions.Regex.Match(title, @"[\.\s](\d{4})[\.\s]");
        if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out var year2))
        {
            // Validate year range (1900-2030)
            if (year2 >= 1900 && year2 <= 2030)
                return year2;
        }

        // Try year at the end (e.g., "Before Sunrise 1995")
        yearMatch = System.Text.RegularExpressions.Regex.Match(title, @"\s(\d{4})$");
        if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out var year3))
        {
            if (year3 >= 1900 && year3 <= 2030)
                return year3;
        }

        return null;
    }

    private string? ExtractQualityFromTitle(string title)
    {
        var lowerTitle = title.ToLowerInvariant();
        if (lowerTitle.Contains("2160p") || lowerTitle.Contains("4k"))
            return "2160p";
        if (lowerTitle.Contains("1080p"))
            return "1080p";
        if (lowerTitle.Contains("720p"))
            return "720p";
        if (lowerTitle.Contains("480p"))
            return "480p";
        return null;
    }

    private string CleanTitle(string title)
    {
        // Replace dots with spaces (common in torrent names)
        title = title.Replace(".", " ");

        // Remove year in parentheses
        title = System.Text.RegularExpressions.Regex.Replace(title, @"\(\d{4}\)", "");

        // Remove quality and format tags (expanded list)
        title = System.Text.RegularExpressions.Regex.Replace(title,
            @"\b(1080p|720p|480p|2160p|4K|HDTV|WEB-DL|WEBRip|BluRay|BDRip|DVDRip|HDRip|" +
            @"x264|x265|H264|H265|HEVC|XviD|AVC|" +
            @"AAC|AC3|DTS|MP3|DD5|DD2|DD|FLAC|Atmos|TrueHD|" +
            @"5\.1|2\.0|7\.1|6\.1|" +
            @"PROPER|REPACK|EXTENDED|UNRATED|DC|REMASTERED|RIP|CAM|HDTS|HDCAM)\b",
            "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Remove content in brackets or parentheses (release groups, etc)
        title = System.Text.RegularExpressions.Regex.Replace(title, @"[\[\(\{].*?[\]\)\}]", "");

        // Remove release group tags (usually after a dash or dot at the end)
        title = System.Text.RegularExpressions.Regex.Replace(title, @"[-\._]\s*[A-Z0-9]+\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Remove extra whitespace
        title = System.Text.RegularExpressions.Regex.Replace(title, @"\s+", " ");

        // Restore common contractions/apostrophes
        title = RestoreApostrophes(title);

        return title.Trim();
    }

    private string RestoreApostrophes(string title)
    {
        // Common contractions without apostrophes
        var contractions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"Dont", "Don't"}, {"Cant", "Can't"}, {"Wont", "Won't"},
            {"Isnt", "Isn't"}, {"Arent", "Aren't"}, {"Wasnt", "Wasn't"}, {"Werent", "Weren't"},
            {"Hasnt", "Hasn't"}, {"Havent", "Haven't"}, {"Hadnt", "Hadn't"},
            {"Doesnt", "Doesn't"}, {"Didnt", "Didn't"},
            {"Wouldnt", "Wouldn't"}, {"Shouldnt", "Shouldn't"}, {"Couldnt", "Couldn't"},
            {"Im", "I'm"}, {"Ive", "I've"}, {"Id", "I'd"}, {"Ill", "I'll"},
            {"Youre", "You're"}, {"Youve", "You've"}, {"Youd", "You'd"}, {"Youll", "You'll"},
            {"Hes", "He's"}, {"Shes", "She's"}, {"Its", "It's"},
            {"Were", "We're"}, {"Weve", "We've"}, {"Wed", "We'd"}, {"Well", "We'll"},
            {"Theyre", "They're"}, {"Theyve", "They've"}, {"Theyd", "They'd"}, {"Theyll", "They'll"},
            {"Thats", "That's"}, {"Theres", "There's"},
            {"Whats", "What's"}, {"Wheres", "Where's"}, {"Whos", "Who's"},
            {"Hows", "How's"}, {"Whys", "Why's"}
        };

        foreach (var kvp in contractions)
        {
            // Word boundary matching to avoid false replacements
            title = System.Text.RegularExpressions.Regex.Replace(
                title,
                $@"\b{kvp.Key}\b",
                kvp.Value,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return title;
    }

    // TPB API Response Model
    // Note: Different TPB endpoints return numbers as strings OR actual numbers
    // This DTO uses JsonConverter to handle both formats
    private class TpbTorrent
    {
        [JsonPropertyName("id")]
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("info_hash")]
        public string InfoHash { get; set; } = string.Empty;

        [JsonPropertyName("seeders")]
        [JsonConverter(typeof(FlexibleIntConverter))]
        public int Seeders { get; set; }

        [JsonPropertyName("leechers")]
        [JsonConverter(typeof(FlexibleIntConverter))]
        public int Leechers { get; set; }

        [JsonPropertyName("size")]
        [JsonConverter(typeof(FlexibleLongConverter))]
        public long Size { get; set; }

        [JsonPropertyName("imdb")]
        public string? ImdbId { get; set; }

        [JsonPropertyName("num_files")]
        [JsonConverter(typeof(FlexibleIntConverter))]
        public int NumFiles { get; set; }
    }

    // Custom converter to handle string or number for int
    private class FlexibleIntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var stringValue = reader.GetString();
                return int.TryParse(stringValue, out var result) ? result : 0;
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetInt32();
            }
            return 0;
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }

    // Custom converter to handle string or number for long
    private class FlexibleLongConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var stringValue = reader.GetString();
                return long.TryParse(stringValue, out var result) ? result : 0;
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetInt64();
            }
            return 0;
        }

        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }

    // Custom converter to handle string or number for string (in case id is a number)
    private class FlexibleStringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString() ?? string.Empty;
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetInt64().ToString();
            }
            return string.Empty;
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
}
