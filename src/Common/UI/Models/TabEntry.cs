namespace Stratum.Common.UI.Models;

/// <summary>
/// Represents a single navigation tab in the ERP tab bar.
/// Managed by <see cref="Stratum.Common.UI.Services.ITabManagerService"/>.
/// </summary>
public sealed record TabEntry(
    Guid Id,
    string Title,
    string Url,
    string? Icon = null,
    bool IsPinned = false);
