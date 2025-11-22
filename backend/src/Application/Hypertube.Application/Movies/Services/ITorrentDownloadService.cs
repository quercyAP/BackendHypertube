using Hypertube.Application.Movies.DTOs;

namespace Hypertube.Application.Movies.Services;

public interface ITorrentDownloadService
{
    Task<Guid> StartDownloadAsync(string torrentUrl, string movieTitle, CancellationToken cancellationToken = default);

    Task<TorrentDownloadProgressDto?> GetProgressAsync(Guid torrentId, CancellationToken cancellationToken = default);

    Task<List<TorrentDownloadProgressDto>> GetAllDownloadsAsync(CancellationToken cancellationToken = default);

    Task StopDownloadAsync(Guid torrentId, CancellationToken cancellationToken = default);

    Task<string?> GetVideoFilePathAsync(Guid torrentId, CancellationToken cancellationToken = default);

    Task<bool> IsReadyForStreamingAsync(Guid torrentId, CancellationToken cancellationToken = default);
}
