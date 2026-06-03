namespace Stratum.Common.UI.Components;

/// <summary>
/// Context passed to <see cref="Kanban{TItem, TColumn}.ColumnHeaderTemplate"/>.
/// </summary>
/// <typeparam name="TColumn">The column definition type.</typeparam>
public record ColumnHeaderContext<TColumn>(
    TColumn Column,
    int CardCount,
    bool IsCollapsed);
