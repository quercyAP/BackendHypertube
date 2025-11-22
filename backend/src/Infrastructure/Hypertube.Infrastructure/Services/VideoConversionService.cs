using FFMpegCore;
using FFMpegCore.Enums;
using Hypertube.Application.Common.Services;
using Microsoft.Extensions.Logging;

namespace Hypertube.Infrastructure.Services;

/// <summary>
/// Service for converting video files using FFmpeg
/// </summary>
public class VideoConversionService : IVideoConversionService
{
    private readonly ILogger<VideoConversionService> _logger;
    private static readonly string[] WebCompatibleFormats = { ".mp4", ".webm" };
    private static readonly string[] ConvertibleFormats = { ".mkv", ".avi", ".mov", ".flv", ".wmv" };

    public VideoConversionService(ILogger<VideoConversionService> logger)
    {
        _logger = logger;
    }

    public async Task<string> ConvertToMp4Async(
        string inputPath,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException($"Input file not found: {inputPath}");
        }

        var extension = Path.GetExtension(inputPath).ToLowerInvariant();
        if (!ConvertibleFormats.Contains(extension))
        {
            throw new InvalidOperationException($"File format {extension} is not supported for conversion");
        }

        _logger.LogInformation("Starting video conversion: {InputPath} -> {OutputPath}", inputPath, outputPath);

        try
        {
            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Convert using FFmpeg
            await FFMpegArguments
                .FromFileInput(inputPath)
                .OutputToFile(outputPath, overwrite: true, options => options
                    .WithVideoCodec(VideoCodec.LibX264) // H.264 codec for broad compatibility
                    .WithAudioCodec(AudioCodec.Aac)       // AAC audio
                    .WithCustomArgument("-movflags faststart") // Enable progressive streaming
                    .WithCustomArgument("-preset fast")    // Fast encoding preset
                    .WithCustomArgument("-crf 23"))       // Quality setting (0-51, lower is better)
                .NotifyOnProgress(percent =>
                {
                    progress?.Report(percent);
                    _logger.LogDebug("Conversion progress: {Percent:F2}%", percent);
                }, TimeSpan.FromSeconds(1))
                .CancellableThrough(cancellationToken)
                .ProcessAsynchronously();

            _logger.LogInformation("Video conversion completed successfully: {OutputPath}", outputPath);
            return outputPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Video conversion failed: {InputPath}", inputPath);

            // Clean up partial output file if it exists
            if (File.Exists(outputPath))
            {
                try
                {
                    File.Delete(outputPath);
                }
                catch (Exception deleteEx)
                {
                    _logger.LogWarning(deleteEx, "Failed to delete partial output file: {OutputPath}", outputPath);
                }
            }

            throw;
        }
    }

    public bool NeedsConversion(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        // If already in web-compatible format, no conversion needed
        if (WebCompatibleFormats.Contains(extension))
        {
            return false;
        }

        // If in a convertible format, conversion is needed
        return ConvertibleFormats.Contains(extension);
    }
}
