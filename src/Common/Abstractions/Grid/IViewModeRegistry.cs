namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// Declares which <see cref="ViewKind"/>s a given list page supports
/// and the property mappings for each (e.g. Kanban groups by "Status").
/// </summary>
public interface IViewModeRegistry
{
    /// <summary>
    /// Returns the default view kind (the first registered view).
    /// </summary>
    ViewKind DefaultView { get; }

    /// <summary>
    /// Returns the descriptors for all supported view modes, in display order.
    /// </summary>
    IReadOnlyList<ViewModeDescriptor> GetSupportedViews();

    /// <summary>
    /// Returns the descriptor for a specific view kind, or <c>null</c> if not supported.
    /// </summary>
    ViewModeDescriptor? GetDescriptor(ViewKind kind);

    /// <summary>
    /// Returns <c>true</c> if the given view kind is supported by this registry.
    /// </summary>
    bool Supports(ViewKind kind);

    /// <summary>
    /// Returns the property mapping for a specific view kind, if one exists.
    /// For example, Kanban might map to "Status" and Calendar to "DueDate".
    /// Returns <c>null</c> when no mapping is configured.
    /// </summary>
    string? GetPropertyMapping(ViewKind kind);
}
