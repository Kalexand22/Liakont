namespace Stratum.Common.UI.Services;

using Stratum.Common.Abstractions.Grid;

/// <summary>
/// Default implementation of <see cref="IViewModeRegistry"/> with a fluent builder API.
/// Subclasses or callers configure supported views via <see cref="ViewModeRegistryBuilder"/>.
/// </summary>
public sealed class ViewModeRegistry : IViewModeRegistry
{
    private readonly IReadOnlyList<ViewModeDescriptor> _descriptors;
    private readonly Dictionary<ViewKind, ViewModeDescriptor> _byKind;
    private readonly Dictionary<ViewKind, string?> _propertyMappings;

    internal ViewModeRegistry(
        IReadOnlyList<ViewModeDescriptor> descriptors,
        Dictionary<ViewKind, string?> propertyMappings)
    {
        if (descriptors.Count == 0)
        {
            throw new ArgumentException("At least one view mode must be registered.", nameof(descriptors));
        }

        _descriptors = descriptors;
        _byKind = descriptors.ToDictionary(d => d.Kind);
        _propertyMappings = propertyMappings;
    }

    /// <inheritdoc />
    public ViewKind DefaultView => _descriptors[0].Kind;

    // ── Static factory methods ──────────────────────────────────

    /// <summary>Table view only (default behavior).</summary>
    public static ViewModeRegistry CreateTableOnly() =>
        new ViewModeRegistryBuilder().AddTable().Build();

    /// <summary>Table + Card views.</summary>
    public static ViewModeRegistry CreateTableCard() =>
        new ViewModeRegistryBuilder().AddTable().AddCard().Build();

    /// <summary>Table + Kanban views.</summary>
    public static ViewModeRegistry CreateTableKanban(string groupByProperty) =>
        new ViewModeRegistryBuilder().AddTable().AddKanban(groupByProperty).Build();

    /// <summary>Table + Calendar views.</summary>
    public static ViewModeRegistry CreateTableCalendar(string dateProperty) =>
        new ViewModeRegistryBuilder().AddTable().AddCalendar(dateProperty).Build();

    /// <summary>All four views: Table + Card + Kanban + Calendar.</summary>
    public static ViewModeRegistry CreateTableCardKanbanCalendar(
        string groupByProperty,
        string dateProperty) =>
        new ViewModeRegistryBuilder()
            .AddTable()
            .AddCard()
            .AddKanban(groupByProperty)
            .AddCalendar(dateProperty)
            .Build();

    /// <inheritdoc />
    public IReadOnlyList<ViewModeDescriptor> GetSupportedViews() => _descriptors;

    /// <inheritdoc />
    public ViewModeDescriptor? GetDescriptor(ViewKind kind) =>
        _byKind.GetValueOrDefault(kind);

    /// <inheritdoc />
    public bool Supports(ViewKind kind) => _byKind.ContainsKey(kind);

    /// <inheritdoc />
    public string? GetPropertyMapping(ViewKind kind) =>
        _propertyMappings.GetValueOrDefault(kind);
}
