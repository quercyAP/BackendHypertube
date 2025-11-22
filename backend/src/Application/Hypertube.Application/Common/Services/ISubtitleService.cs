namespace Hypertube.Application.Common.Services;

public class SubtitleInfo
{
    public string Id { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string LanguageName { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
}

public interface ISubtitleService
{
    Task<List<SubtitleInfo>> SearchSubtitlesAsync(
        string imdbId,
        string[] languages,
        CancellationToken cancellationToken = default);

    Task<string> DownloadSubtitleAsync(
        string subtitleId,
        string language,
        string outputDirectory,
        CancellationToken cancellationToken = default);

    Task<string> ConvertSrtToWebVttAsync(
        string srtPath,
        CancellationToken cancellationToken = default);

    string? GetCachedSubtitlePath(Guid movieId, string language);
}
