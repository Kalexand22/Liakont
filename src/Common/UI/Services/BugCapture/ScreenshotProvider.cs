namespace Stratum.Common.UI.Services.BugCapture;

using Microsoft.JSInterop;
using Stratum.Common.Infrastructure.BugCapture;

public sealed class ScreenshotProvider : IScreenshotProvider
{
    private static readonly string TempDir =
        Path.Combine(Path.GetTempPath(), "stratum-bugcapture", "screenshots");

    private readonly IJSRuntime _js;

    public ScreenshotProvider(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<MediaCapture> CapturePageAsync(string description = "")
    {
        var base64 = await _js.InvokeAsync<string>("stratumUI.bugCapture.captureScreenshot");
        return await SaveScreenshotAsync(base64, description);
    }

    public async Task<MediaCapture> CaptureRegionAsync(string selector, string description = "")
    {
        string base64;

        if (!string.IsNullOrEmpty(selector))
        {
            base64 = await _js.InvokeAsync<string>(
                "stratumUI.bugCapture.captureScreenshot", selector);
        }
        else
        {
            base64 = await _js.InvokeAsync<string>("stratumUI.bugCapture.captureRegion");
        }

        return await SaveScreenshotAsync(base64, description);
    }

    public async Task<IReadOnlyList<MediaCapture>> ExtractFramesAsync(
        byte[] videoBytes, IReadOnlyList<double> timestamps, IReadOnlyList<string> descriptions)
    {
        if (videoBytes.Length == 0 || timestamps.Count == 0)
        {
            return [];
        }

        // Uses the video blob kept in JS memory after stopScreenRecording().
        // No need to transfer bytes back — JS still has the original blob.
        var frames = await _js.InvokeAsync<string[]>(
            "stratumUI.bugCapture.extractFramesFromLastRecording", timestamps.ToArray());

        var results = new List<MediaCapture>();

        for (var i = 0; i < frames.Length; i++)
        {
            if (string.IsNullOrEmpty(frames[i]))
            {
                continue;
            }

            var desc = i < descriptions.Count ? descriptions[i] : $"Frame at {timestamps[i]:F1}s";
            var capture = await SaveScreenshotAsync(frames[i], desc);
            results.Add(capture);
        }

        return results;
    }

    private static async Task<MediaCapture> SaveScreenshotAsync(string base64, string description)
    {
        Directory.CreateDirectory(TempDir);

        var id = Guid.NewGuid();
        var fileName = $"{id:N}.png";
        var filePath = Path.Combine(TempDir, fileName);
        var data = Convert.FromBase64String(base64);

        await File.WriteAllBytesAsync(filePath, data);

        return new MediaCapture
        {
            Id = id,
            MediaType = "screenshot",
            FilePath = filePath,
            FileName = fileName,
            MimeType = "image/png",
            FileSizeBytes = data.Length,
            CapturedAt = DateTimeOffset.UtcNow,
            Description = string.IsNullOrEmpty(description) ? null : description,
            Sequence = 0,
        };
    }
}
