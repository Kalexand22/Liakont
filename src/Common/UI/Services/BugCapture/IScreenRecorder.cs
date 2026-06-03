namespace Stratum.Common.UI.Services.BugCapture;

public interface IScreenRecorder
{
    bool IsRecording { get; }

    Task StartAsync();

    Task<byte[]> StopAsync();
}
