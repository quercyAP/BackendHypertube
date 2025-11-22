namespace Hypertube.Application.Common.Services;

public interface IVideoRemuxService
{
    Task<string> RemuxToMp4Async(
        string inputPath,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
