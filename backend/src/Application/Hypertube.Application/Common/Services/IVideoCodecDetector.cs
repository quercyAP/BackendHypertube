namespace Hypertube.Application.Common.Services;

public class VideoCodecInfo
{
    public string VideoCodec { get; set; } = string.Empty;
    public string AudioCodec { get; set; } = string.Empty;
    public string ContainerFormat { get; set; } = string.Empty;
    public bool IsWebCompatible { get; set; }
}

public interface IVideoCodecDetector
{
    Task<VideoCodecInfo?> DetectCodecsAsync(string filePath, CancellationToken cancellationToken = default);

    Task<bool> CanBeRemuxedAsync(string filePath, CancellationToken cancellationToken = default);
}
