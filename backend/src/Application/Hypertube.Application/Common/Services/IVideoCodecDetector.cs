using System.Collections.Generic;

namespace Hypertube.Application.Common.Services;

public class VideoCodecInfo
{
    public string VideoCodec { get; set; } = string.Empty;
    public string AudioCodec { get; set; } = string.Empty;
    public string ContainerFormat { get; set; } = string.Empty;
    public bool IsWebCompatible { get; set; }

    // Internal subtitle tracks discovered during codec detection (e.g. MKV/MP4 embedded subtitles).
    public List<SubtitleTrackInfo> SubtitleTracks { get; set; } = new();
}

public class SubtitleTrackInfo
{
    /// <summary>
    /// Stream index as reported by ffprobe/FFMpegCore.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Subtitle codec name (e.g. "subrip", "ass", "hdmv_pgs_subtitle").
    /// </summary>
    public string Codec { get; set; } = string.Empty;
}

public interface IVideoCodecDetector
{
    Task<VideoCodecInfo?> DetectCodecsAsync(string filePath, CancellationToken cancellationToken = default);

    Task<bool> CanBeRemuxedAsync(string filePath, CancellationToken cancellationToken = default);
}
