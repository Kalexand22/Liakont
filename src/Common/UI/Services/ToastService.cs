namespace Stratum.Common.UI.Services;

using Stratum.Common.UI.Models;

/// <summary>
/// Scoped toast notification queue. Manages up to 3 simultaneous toasts and
/// auto-dismisses them after their configured duration.
/// </summary>
internal sealed class ToastService : IToastService, IDisposable
{
    private readonly List<ToastMessage> _toasts = [];
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cts = new();

    public event Action? OnToastsChanged;

    public IReadOnlyList<ToastMessage> GetActiveToasts()
    {
        lock (_lock)
        {
            return [.. _toasts];
        }
    }

    public void Show(string message, Severity severity, int duration = 5000, bool dismissible = true)
    {
        var toast = new ToastMessage(Guid.NewGuid(), message, severity, duration, dismissible);

        lock (_lock)
        {
            _toasts.Add(toast);

            // Keep at most 3 — evict the oldest when exceeded.
            while (_toasts.Count > 3)
            {
                _toasts.RemoveAt(0);
            }
        }

        OnToastsChanged?.Invoke();

        if (duration > 0)
        {
            var id = toast.Id;
            var token = _cts.Token;
            _ = Task.Delay(duration, token)
                    .ContinueWith(
                        _ => Dismiss(id),
                        CancellationToken.None,
                        TaskContinuationOptions.NotOnCanceled,
                        TaskScheduler.Default);
        }
    }

    public void Dismiss(Guid id)
    {
        bool removed;

        lock (_lock)
        {
            removed = _toasts.RemoveAll(t => t.Id == id) > 0;
        }

        if (removed)
        {
            OnToastsChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
