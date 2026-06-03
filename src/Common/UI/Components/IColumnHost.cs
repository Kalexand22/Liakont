namespace Stratum.Common.UI.Components;

/// <summary>
/// Internal contract that allows <see cref="Column{TItem}"/> to register itself
/// with the parent <see cref="SimpleTable{TItem}"/> during initialization.
/// </summary>
internal interface IColumnHost<TItem>
{
    void RegisterColumn(Column<TItem> column);
}
