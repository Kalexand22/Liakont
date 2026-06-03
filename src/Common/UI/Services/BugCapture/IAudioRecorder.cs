namespace Stratum.Common.UI.Services.BugCapture;

public interface IAudioRecorder
{
    bool IsRecording { get; }

    float AudioLevel { get; }

    Task StartAsync();

    Task<byte[]> StopAsync();
}
