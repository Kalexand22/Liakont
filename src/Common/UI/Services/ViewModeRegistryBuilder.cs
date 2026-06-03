namespace Stratum.Common.UI.Services;

using Stratum.Common.Abstractions.Grid;

/// <summary>
/// Fluent builder for constructing a <see cref="ViewModeRegistry"/>.
/// </summary>
public sealed class ViewModeRegistryBuilder
{
    private readonly List<ViewModeDescriptor> _descriptors = [];
    private readonly Dictionary<ViewKind, string?> _propertyMappings = [];

    /// <summary>
    /// Registers the Table view mode.
    /// </summary>
    public ViewModeRegistryBuilder AddTable(string label = "Tableau", string icon = "bi-table")
    {
        GuardDuplicate(ViewKind.Table);
        _descriptors.Add(new ViewModeDescriptor(ViewKind.Table, label, icon));
        return this;
    }

    /// <summary>
    /// Registers the Card view mode.
    /// </summary>
    public ViewModeRegistryBuilder AddCard(string label = "Cartes", string icon = "bi-grid-3x3-gap")
    {
        GuardDuplicate(ViewKind.Card);
        _descriptors.Add(new ViewModeDescriptor(ViewKind.Card, label, icon));
        return this;
    }

    /// <summary>
    /// Registers the Kanban view mode with the required group-by property.
    /// </summary>
    public ViewModeRegistryBuilder AddKanban(
        string groupByProperty,
        string label = "Kanban",
        string icon = "bi-kanban")
    {
        GuardDuplicate(ViewKind.Kanban);
        _descriptors.Add(new ViewModeDescriptor(ViewKind.Kanban, label, icon, RequiredPropertyHint: "GroupBy property"));
        _propertyMappings[ViewKind.Kanban] = groupByProperty;
        return this;
    }

    /// <summary>
    /// Registers the Calendar view mode with the required date property.
    /// </summary>
    public ViewModeRegistryBuilder AddCalendar(
        string dateProperty,
        string label = "Calendrier",
        string icon = "bi-calendar3")
    {
        GuardDuplicate(ViewKind.Calendar);
        _descriptors.Add(new ViewModeDescriptor(ViewKind.Calendar, label, icon, RequiredPropertyHint: "Date property"));
        _propertyMappings[ViewKind.Calendar] = dateProperty;
        return this;
    }

    /// <summary>
    /// Registers a custom view mode.
    /// </summary>
    public ViewModeRegistryBuilder Add(ViewModeDescriptor descriptor, string? propertyMapping = null)
    {
        GuardDuplicate(descriptor.Kind);
        _descriptors.Add(descriptor);
        if (propertyMapping is not null)
        {
            _propertyMappings[descriptor.Kind] = propertyMapping;
        }

        return this;
    }

    /// <summary>
    /// Builds the <see cref="ViewModeRegistry"/> from the registered view modes.
    /// </summary>
    public ViewModeRegistry Build() => new(_descriptors, _propertyMappings);

    private void GuardDuplicate(ViewKind kind)
    {
        if (_descriptors.Exists(d => d.Kind == kind))
        {
            throw new InvalidOperationException($"ViewKind.{kind} is already registered.");
        }
    }
}
