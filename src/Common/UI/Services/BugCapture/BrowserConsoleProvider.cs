namespace Stratum.Common.UI.Services.BugCapture;

using Microsoft.JSInterop;
using Stratum.Common.Infrastructure.BugCapture;

public sealed class BrowserConsoleProvider : IBrowserConsoleProvider
{
    private readonly IJSRuntime _js;

    public BrowserConsoleProvider(IJSRuntime js)
    {
        _js = js;
    }

    public async Task StartAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("stratumUI.bugCapture.startConsoleCapture");
        }
        catch
        {
            // Silent failure — never blocks capture session.
        }
    }

    public async Task StopAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("stratumUI.bugCapture.stopConsoleCapture");
        }
        catch
        {
            // Silent failure.
        }
    }

    public async Task<IReadOnlyList<BrowserConsoleEntry>> GetSnapshotAsync()
    {
        try
        {
            var entries = await _js.InvokeAsync<JsConsoleEntry[]>(
                "stratumUI.bugCapture.getConsoleLogs");

            if (entries is null || entries.Length == 0)
            {
                return [];
            }

            var result = new List<BrowserConsoleEntry>(entries.Length);

            foreach (var entry in entries)
            {
                if (!DateTimeOffset.TryParse(entry.Timestamp, out var ts))
                {
                    ts = DateTimeOffset.UtcNow;
                }

                result.Add(new BrowserConsoleEntry
                {
                    Timestamp = ts,
                    Level = entry.Level ?? "log",
                    Message = entry.Message ?? string.Empty,
                });
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

    private sealed record JsConsoleEntry
    {
        public string? Timestamp { get; init; }

        public string? Level { get; init; }

        public string? Message { get; init; }
    }
}
