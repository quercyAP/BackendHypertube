using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hypertube.Application.Common.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hypertube.Infrastructure.Services;

/// <summary>
/// Service for interacting with OpenSubtitles API
/// </summary>
public class SubtitleService : ISubtitleService
{
    private readonly ILogger<SubtitleService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _userAgent;
    private readonly string _subtitlesDirectory;

    public SubtitleService(
        ILogger<SubtitleService> logger,
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _logger = logger;
        _httpClient = httpClient;

        _apiKey = Environment.GetEnvironmentVariable("OPENSUBTITLES_API_KEY")
            ?? throw new InvalidOperationException("OPENSUBTITLES_API_KEY environment variable is required");

        _baseUrl = Environment.GetEnvironmentVariable("OPENSUBTITLES_BASE_URL")
            ?? "https://api.opensubtitles.com/api/v1";

        _userAgent = Environment.GetEnvironmentVariable("OPENSUBTITLES_USER_AGENT")
            ?? "Hypertube v1.0";

        // Get download directory from environment or default to downloads/subtitles
        var downloadDir = Environment.GetEnvironmentVariable("DOWNLOAD_DIRECTORY")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "downloads");

        _subtitlesDirectory = Path.Combine(downloadDir, "subtitles");

        // Create subtitles directory if it doesn't exist
        Directory.CreateDirectory(_subtitlesDirectory);

        // Configure HttpClient
        _httpClient.BaseAddress = new Uri(_baseUrl);
        _httpClient.DefaultRequestHeaders.Add("Api-Key", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", _userAgent);
    }

    public async Task<List<SubtitleInfo>> SearchSubtitlesAsync(
        string imdbId,
        string[] languages,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Remove "tt" prefix if present
            var cleanImdbId = imdbId.Replace("tt", "");

            // Build query parameters
            var languagesParam = string.Join(",", languages);
            var url = $"/subtitles?imdb_id={cleanImdbId}&languages={languagesParam}";

            _logger.LogInformation("Searching subtitles for IMDb ID: {ImdbId}, Languages: {Languages}",
                imdbId, languagesParam);

            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OpenSubtitles API returned status code: {StatusCode}", response.StatusCode);
                return new List<SubtitleInfo>();
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<OpenSubtitlesSearchResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result?.Data == null || result.Data.Count == 0)
            {
                _logger.LogInformation("No subtitles found for IMDb ID: {ImdbId}", imdbId);
                return new List<SubtitleInfo>();
            }

            // Convert to SubtitleInfo and group by language (take first result per language)
            var subtitles = result.Data
                .GroupBy(s => s.Attributes?.Language)
                .Where(g => g.Key != null)
                .Select(g => g.First())
                .Select(s =>
                {
                    var fileId = s.Attributes?.Files?.FirstOrDefault()?.FileId;
                    return new SubtitleInfo
                    {
                        Id = fileId.HasValue ? fileId.Value.ToString() : string.Empty,
                        Language = s.Attributes?.Language ?? string.Empty,
                        LanguageName = GetLanguageName(s.Attributes?.Language ?? string.Empty),
                        DownloadUrl = fileId.HasValue ? fileId.Value.ToString() : string.Empty,
                        Format = "srt"
                    };
                })
                .Where(s => !string.IsNullOrEmpty(s.Id))
                .ToList();

            _logger.LogInformation("Found {Count} subtitle languages for IMDb ID: {ImdbId}",
                subtitles.Count, imdbId);

            return subtitles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching subtitles for IMDb ID: {ImdbId}", imdbId);
            return new List<SubtitleInfo>();
        }
    }

    public async Task<string> DownloadSubtitleAsync(
        string subtitleId,
        string language,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Downloading subtitle: {SubtitleId}, Language: {Language}",
                subtitleId, language);

            // Request download link
            var downloadLinkResponse = await _httpClient.PostAsync(
                "/download",
                new StringContent(JsonSerializer.Serialize(new { file_id = int.Parse(subtitleId) }),
                    Encoding.UTF8, "application/json"),
                cancellationToken);

            if (!downloadLinkResponse.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to get download link: {downloadLinkResponse.StatusCode}");
            }

            var downloadLinkContent = await downloadLinkResponse.Content.ReadAsStringAsync(cancellationToken);
            var downloadLinkResult = JsonSerializer.Deserialize<OpenSubtitlesDownloadResponse>(
                downloadLinkContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (string.IsNullOrEmpty(downloadLinkResult?.Link))
            {
                throw new Exception("Download link not found in response");
            }

            // Download the subtitle file
            var subtitleResponse = await _httpClient.GetAsync(downloadLinkResult.Link, cancellationToken);
            subtitleResponse.EnsureSuccessStatusCode();

            var subtitleContent = await subtitleResponse.Content.ReadAsStringAsync(cancellationToken);

            // Save to file
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, $"{language}.srt");
            await File.WriteAllTextAsync(outputPath, subtitleContent, cancellationToken);

            _logger.LogInformation("Subtitle downloaded successfully: {OutputPath}", outputPath);

            return outputPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading subtitle: {SubtitleId}", subtitleId);
            throw;
        }
    }

    public async Task<string> ConvertSrtToWebVttAsync(
        string srtPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(srtPath))
            {
                throw new FileNotFoundException($"SRT file not found: {srtPath}");
            }

            _logger.LogInformation("Converting SRT to WebVTT: {SrtPath}", srtPath);

            var srtContent = await File.ReadAllTextAsync(srtPath, cancellationToken);

            // Convert SRT to WebVTT
            var vttContent = ConvertSrtToVtt(srtContent);

            // Save WebVTT file
            var vttPath = Path.ChangeExtension(srtPath, ".vtt");
            await File.WriteAllTextAsync(vttPath, vttContent, Encoding.UTF8, cancellationToken);

            _logger.LogInformation("Conversion completed: {VttPath}", vttPath);

            return vttPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting SRT to WebVTT: {SrtPath}", srtPath);
            throw;
        }
    }

    public string? GetCachedSubtitlePath(Guid movieId, string language)
    {
        var vttPath = Path.Combine(_subtitlesDirectory, movieId.ToString(), $"{language}.vtt");

        if (File.Exists(vttPath))
        {
            _logger.LogInformation("Found cached subtitle: {VttPath}", vttPath);
            return vttPath;
        }

        return null;
    }

    private string ConvertSrtToVtt(string srtContent)
    {
        // WebVTT header
        var vtt = new StringBuilder("WEBVTT\n\n");

        // Replace SRT timestamp format with WebVTT format
        // SRT: 00:00:01,000 --> 00:00:04,000
        // VTT: 00:00:01.000 --> 00:00:04.000
        var converted = Regex.Replace(srtContent, @"(\d{2}:\d{2}:\d{2}),(\d{3})", "$1.$2");

        // Remove subtitle indices (numbers at the start of each subtitle block)
        // Keep the timestamps and text
        var lines = converted.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var isSubtitleIndex = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Check if line is a subtitle index (just a number)
            if (int.TryParse(line, out _))
            {
                isSubtitleIndex = true;
                continue;
            }

            // Check if line contains timestamp
            if (line.Contains("-->"))
            {
                vtt.AppendLine(line);
                isSubtitleIndex = false;
                continue;
            }

            // Add subtitle text and blank lines
            if (!isSubtitleIndex)
            {
                vtt.AppendLine(line);
            }
        }

        return vtt.ToString();
    }

    private string GetLanguageName(string languageCode)
    {
        return languageCode.ToLowerInvariant() switch
        {
            "en" => "English",
            "fr" => "French",
            "es" => "Spanish",
            "de" => "German",
            "it" => "Italian",
            "pt" => "Portuguese",
            "ru" => "Russian",
            "zh" => "Chinese",
            "ja" => "Japanese",
            "ko" => "Korean",
            _ => languageCode.ToUpperInvariant()
        };
    }

    // OpenSubtitles API response models
    private class OpenSubtitlesSearchResponse
    {
        public List<OpenSubtitlesSubtitle>? Data { get; set; }
    }

    private class OpenSubtitlesSubtitle
    {
        public OpenSubtitlesAttributes? Attributes { get; set; }
    }

    private class OpenSubtitlesAttributes
    {
        public string? Language { get; set; }
        public List<OpenSubtitlesFile>? Files { get; set; }
    }

    private class OpenSubtitlesFile
    {
        public int? FileId { get; set; }
    }

    private class OpenSubtitlesDownloadResponse
    {
        public string? Link { get; set; }
    }
}
