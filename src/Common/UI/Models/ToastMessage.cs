namespace Stratum.Common.UI.Models;

/// <summary>An active toast notification managed by <see cref="Stratum.Common.UI.Services.IToastService"/>.</summary>
public sealed record ToastMessage(
    Guid Id,
    string Message,
    Severity Severity,
    int Duration,
    bool Dismissible);
