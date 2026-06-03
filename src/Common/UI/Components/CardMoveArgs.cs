namespace Stratum.Common.UI.Components;

/// <summary>
/// Arguments supplied to <see cref="Kanban{TItem, TColumn}.OnCardMove"/> when the user
/// moves a card to a different column or reorders it within the same column.
/// </summary>
/// <typeparam name="TItem">The card item type.</typeparam>
public record CardMoveArgs<TItem>(
    TItem Item,
    object FromColumnKey,
    object ToColumnKey,
    int FromIndex,
    int ToIndex);
