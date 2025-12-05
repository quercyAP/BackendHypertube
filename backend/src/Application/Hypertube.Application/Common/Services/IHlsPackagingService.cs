using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hypertube.Application.Common.Services;

public interface IHlsPackagingService
{
    /// <summary>
    /// Returns an HTTP-accessible playlist URL (e.g. /hls/{torrentId}/index.m3u8) for the given torrent.
    /// Generates the manifest + segments if they do not exist yet.
    /// </summary>
    Task<string?> GetOrCreatePlaylistAsync(Guid torrentId, CancellationToken cancellationToken = default);
}
