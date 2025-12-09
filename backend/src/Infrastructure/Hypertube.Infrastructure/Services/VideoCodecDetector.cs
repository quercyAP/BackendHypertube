using FFMpegCore;
using Hypertube.Application.Common.Services;
using Microsoft.Extensions.Logging;

namespace Hypertube.Infrastructure.Services;

/// <summary>
/// Service for detecting video and audio codecs using FFprobe
/// </summary>
public class VideoCodecDetector : IVideoCodecDetector
{
    private readonly ILogger<VideoCodecDetector> _logger;

    // Web-compatible codecs
    private static readonly string[] WebCompatibleVideoCodecs = { "h264", "vp8", "vp9", "av1" };
    private static readonly string[] WebCompatibleAudioCodecs = { "aac", "opus", "vorbis" };
    private static readonly string[] WebCompatibleContainers = { "mp4", "webm", "mov" };

    public VideoCodecDetector(ILogger<VideoCodecDetector> logger)
    {
        _logger = logger;
    }

    public async Task<VideoCodecInfo?> DetectCodecsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            _logger.LogWarning("File not found for codec detection: {FilePath}", filePath);
            return null;
        }

        try
        {
            _logger.LogInformation("Detecting codecs for file: {FilePath}", filePath);

            // Use FFProbe to analyze the media file
            var mediaInfo = await FFProbe.AnalyseAsync(filePath, null, cancellationToken);

            if (mediaInfo == null)
            {
                _logger.LogWarning("Failed to analyze media file: {FilePath}", filePath);
                return null;
            }

            // Extract video codec
            var videoStream = mediaInfo.VideoStreams.FirstOrDefault();
            var videoCodec = videoStream?.CodecName?.ToLowerInvariant() ?? string.Empty;

            // Extract audio codec
            var audioStream = mediaInfo.AudioStreams.FirstOrDefault();
            var audioCodec = audioStream?.CodecName?.ToLowerInvariant() ?? string.Empty;

            // Extract container format
            var containerFormat = mediaInfo.Format.FormatName?.ToLowerInvariant() ?? string.Empty;

            // Extract internal subtitle streams (if any)
            var subtitleTracks = new List<SubtitleTrackInfo>();
            foreach (var subtitleStream in mediaInfo.SubtitleStreams)
            {
                try
                {
                    subtitleTracks.Add(new SubtitleTrackInfo
                    {
                        Index = subtitleStream.Index,
                        Codec = subtitleStream.CodecName?.ToLowerInvariant() ?? string.Empty
                    });
                }
                catch
                {
                    // Best-effort: ignore malformed subtitle stream metadata
                }
            }

            // Determine if web-compatible
            var isWebCompatible = IsWebCompatibleCodecs(videoCodec, audioCodec, containerFormat);

            var codecInfo = new VideoCodecInfo
            {
                VideoCodec = videoCodec,
                AudioCodec = audioCodec,
                ContainerFormat = containerFormat,
                IsWebCompatible = isWebCompatible,
                SubtitleTracks = subtitleTracks
            };

            _logger.LogInformation(
                "Detected codecs - Video: {VideoCodec}, Audio: {AudioCodec}, Container: {Container}, WebCompatible: {IsWebCompatible}, Subtitles: {SubtitleCount}",
                videoCodec, audioCodec, containerFormat, isWebCompatible, subtitleTracks.Count);

            return codecInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting codecs for file: {FilePath}", filePath);
            return null;
        }
    }

    public async Task<bool> CanBeRemuxedAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var codecInfo = await DetectCodecsAsync(filePath, cancellationToken);

        if (codecInfo == null)
        {
            return false;
        }

        // Check if file has H.264 video and AAC audio (can be fast-remuxed)
        var canRemux = codecInfo.VideoCodec == "h264" && codecInfo.AudioCodec == "aac";

        _logger.LogInformation(
            "File {FilePath} can be remuxed: {CanRemux} (Video: {VideoCodec}, Audio: {AudioCodec})",
            filePath, canRemux, codecInfo.VideoCodec, codecInfo.AudioCodec);

        return canRemux;
    }

    private bool IsWebCompatibleCodecs(string videoCodec, string audioCodec, string containerFormat)
    {
        var videoCompatible = WebCompatibleVideoCodecs.Contains(videoCodec);
        var audioCompatible = WebCompatibleAudioCodecs.Contains(audioCodec);
        var containerCompatible = WebCompatibleContainers.Any(c => containerFormat.Contains(c));

        return videoCompatible && audioCompatible && containerCompatible;
    }
}
