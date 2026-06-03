namespace Stratum.Common.UI.Services.BugCapture;

using Stratum.Common.Infrastructure.BugCapture;

public interface IScreenshotProvider
{
    Task<MediaCapture> CapturePageAsync(string description = "");

    Task<MediaCapture> CaptureRegionAsync(string selector, string description = "");

    /// <summary>
    /// Extracts frames from a video file at the given timestamps via JS interop.
    /// </summary>
    Task<IReadOnlyList<MediaCapture>> ExtractFramesAsync(byte[] videoBytes, IReadOnlyList<double> timestamps, IReadOnlyList<string> descriptions);
}
