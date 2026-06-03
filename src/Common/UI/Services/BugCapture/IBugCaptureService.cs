namespace Stratum.Common.UI.Services.BugCapture;

using Stratum.Common.Infrastructure.BugCapture;

public interface IBugCaptureService
{
    event EventHandler? StateChanged;

    CaptureState State { get; }

    CaptureBundle? PreparedBundle { get; }

    ICaptureSession StartSession(CaptureType type);

    Task PrepareAsync(string comment, IReadOnlyList<string> tags);

    Task SubmitPreparedAsync();

    void UpdatePreparedBundle(string title, string comment, IReadOnlyList<string> tags, string? aiSummary = null, string? aiSteps = null);

    void ReturnToEditing();

    void CancelSession();
}
