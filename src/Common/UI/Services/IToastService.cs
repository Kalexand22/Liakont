namespace Stratum.Common.UI.Services;

using Stratum.Common.UI.Models;

/// <summary>
/// Manages the in-circuit toast notification queue.
/// Register via <c>AddCommonUI()</c>. Place a single <c>&lt;Toast /&gt;</c>
/// in the layout to render the active toasts.
/// </summary>
public interface IToastService
{
    /// <summary>Fired on the circuit's synchronisation context when the toast list changes.</summary>
    event Action? OnToastsChanged;

    /// <summary>Returns a snapshot of the currently active toasts (max 3).</summary>
    IReadOnlyList<ToastMessage> GetActiveToasts();

    /// <summary>Enqueues a new toast. Auto-dismissed after <paramref name="duration"/> ms.</summary>
    /// <param name="duration">Display duration in ms. 0 = persistent until dismissed.</param>
    void Show(string message, Severity severity, int duration = 5000, bool dismissible = true);

    /// <summary>Dismisses the toast with the given <paramref name="id"/>.</summary>
    void Dismiss(Guid id);
}
