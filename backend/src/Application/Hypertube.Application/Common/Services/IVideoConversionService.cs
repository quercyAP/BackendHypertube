namespace Hypertube.Application.Common.Services;

public interface IVideoConversionService
{
    Task<string> ConvertToMp4Async(
        string inputPath,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    bool NeedsConversion(string filePath);
}
