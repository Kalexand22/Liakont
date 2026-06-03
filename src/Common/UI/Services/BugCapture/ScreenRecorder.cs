namespace Stratum.Common.UI.Services.BugCapture;

using Microsoft.JSInterop;

public sealed class ScreenRecorder : IScreenRecorder
{
    private readonly IJSRuntime _js;
    private bool _isRecording;

    public ScreenRecorder(IJSRuntime js)
    {
        _js = js;
    }

    public bool IsRecording => _isRecording;

    public async Task StartAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("stratumUI.bugCapture.startScreenRecording");
            _isRecording = true;
        }
        catch (JSException)
        {
            _isRecording = false;
            throw;
        }
    }

    public async Task<byte[]> StopAsync()
    {
        if (!_isRecording)
        {
            return [];
        }

        _isRecording = false;

        try
        {
            var streamRef = await _js.InvokeAsync<IJSStreamReference>(
                "stratumUI.bugCapture.stopScreenRecording");

            if (streamRef is null)
            {
                return [];
            }

            await using var stream = await streamRef.OpenReadStreamAsync(
                maxAllowedSize: 104_857_600); // 100 MB

            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            return ms.ToArray();
        }
        catch (JSException)
        {
            return [];
        }
    }
}
