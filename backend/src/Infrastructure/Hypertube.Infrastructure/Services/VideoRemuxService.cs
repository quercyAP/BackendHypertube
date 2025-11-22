using FFMpegCore;
using Hypertube.Application.Common.Services;
using Microsoft.Extensions.Logging;

namespace Hypertube.Infrastructure.Services;

/// <summary>
/// Service for fast remuxing video files using FFmpeg codec copy
/// </summary>
public class VideoRemuxService : IVideoRemuxService
{
    private readonly ILogger<VideoRemuxService> _logger;

    public VideoRemuxService(ILogger<VideoRemuxService> logger)
    {
        _logger = logger;
    }

    public async Task<string> RemuxToMp4Async(
        string inputPath,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException($"Input file not found: {inputPath}");
        }

        _logger.LogInformation("Starting fast remux (codec copy): {InputPath} -> {OutputPath}", inputPath, outputPath);

        try
        {
            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Remux using FFmpeg with codec copy (no transcoding)
            // This is much faster than conversion (30 seconds vs 1-4 hours)
            await FFMpegArguments
                .FromFileInput(inputPath)
                .OutputToFile(outputPath, overwrite: true, options => options
                    .CopyChannel()  // Copy all streams without re-encoding (equivalent to -c copy)
                    .WithCustomArgument("-movflags faststart"))  // Enable progressive streaming for MP4
                .NotifyOnProgress(percent =>
                {
                    progress?.Report(percent);
                    _logger.LogDebug("Remux progress: {Percent:F2}%", percent);
                }, TimeSpan.FromSeconds(1))
                .CancellableThrough(cancellationToken)
                .ProcessAsynchronously();

            _logger.LogInformation("Fast remux completed successfully: {OutputPath}", outputPath);
            return outputPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fast remux failed: {InputPath}", inputPath);

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
}
