namespace Stratum.Common.UI.Services.BugCapture;

using Microsoft.JSInterop;

public sealed class AudioRecorder : IAudioRecorder, IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<AudioRecorder>? _dotNetRef;
    private float _audioLevel;
    private bool _isRecording;

    public AudioRecorder(IJSRuntime js)
    {
        _js = js;
    }

    public bool IsRecording => _isRecording;

    public float AudioLevel => _audioLevel;

    public async Task StartAsync()
    {
        _dotNetRef ??= DotNetObjectReference.Create(this);
        _audioLevel = 0f;

        try
        {
            await _js.InvokeVoidAsync("stratumUI.bugCapture.startAudioRecording", _dotNetRef);
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
        _audioLevel = 0f;

        try
        {
            var streamRef = await _js.InvokeAsync<IJSStreamReference>(
                "stratumUI.bugCapture.stopAudioRecording");

            if (streamRef is null)
            {
                return [];
            }

            await using var stream = await streamRef.OpenReadStreamAsync(
                maxAllowedSize: 52_428_800); // 50 MB

            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            return ms.ToArray();
        }
        catch (JSException)
        {
            return [];
        }
    }

    [JSInvokable]
    public void OnAudioLevel(float level)
    {
        _audioLevel = level;
    }

    public ValueTask DisposeAsync()
    {
        _dotNetRef?.Dispose();
        _dotNetRef = null;
        return ValueTask.CompletedTask;
    }
}
