namespace Stratum.Common.UI.Services;

using System;
using System.Collections.Generic;

/// <summary>
/// Grid-facing adapter over <see cref="IPersistentSelectionService{TKey}"/>.
/// Hides <c>TKey</c> from generic grid components that only know
/// <typeparamref name="TItem"/>; the implementation owns the
/// <c>TItem → TKey</c> projection.
/// <para>
/// Consumers instantiate <see cref="PersistentSelectionBinding{TItem, TKey}"/>
/// from a page code-behind (injecting the typed service and providing a key
/// selector lambda) and pass it to <c>StratumDataGrid.PersistentSelection</c>.
/// </para>
/// </summary>
/// <typeparam name="TItem">Row item type displayed by the grid.</typeparam>
public interface IPersistentSelectionBinding<in TItem>
{
    /// <summary>Raised after any mutation so the grid can re-render counters.</summary>
    event Action? Changed;

    /// <summary>Total number of persisted items across all pages/filters.</summary>
    int TotalCount { get; }

    /// <summary>Returns <c>true</c> if <paramref name="item"/>'s key is persisted.</summary>
    bool Contains(TItem item);

    /// <summary>Adds <paramref name="item"/>'s key to the persisted set.</summary>
    void Add(TItem item);

    /// <summary>Adds every item's key in <paramref name="items"/>. Returns the number of keys that were newly added.</summary>
    int AddRange(IEnumerable<TItem> items);

    /// <summary>Removes every item's key in <paramref name="items"/>. Returns the number of keys that were actually removed.</summary>
    int RemoveRange(IEnumerable<TItem> items);

    /// <summary>Clears all persisted keys.</summary>
    void Clear();
}
