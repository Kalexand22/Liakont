namespace Stratum.Common.UI.Services.BugCapture;

using Stratum.Common.Infrastructure.BugCapture;

public interface ICaptureSession
{
    Task AddScreenshotAsync(string description = "");

    Task AddAudioAsync(string description = "");

    Task AddScreenRecordingAsync(string description = "");

    void AddComment(string comment);

    void AddTag(string tag);

    CaptureBundle GetCurrentBundle();
}
