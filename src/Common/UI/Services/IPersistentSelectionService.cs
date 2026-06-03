namespace Stratum.Common.UI.Services;

using System;
using System.Collections.Generic;

/// <summary>
/// Session-scoped selection store that survives data-driven changes like paging
/// and filtering. Consumers (grid + toolbar UI) subscribe to
/// <see cref="SelectionChanged"/> and mutate the set via <see cref="Add"/>,
/// <see cref="Remove"/>, <see cref="AddRange"/>, and <see cref="Clear"/>.
/// <para>
/// Registered as an open generic scoped service (one instance per Blazor circuit
/// per <typeparamref name="TKey"/>): the selection resets on full page reload,
/// as validated with user (GUX03 scope = session only, no DB persistence).
/// </para>
/// </summary>
/// <typeparam name="TKey">Entity key type (e.g. <see cref="Guid"/>, <see cref="int"/>, <see cref="string"/>).</typeparam>
public interface IPersistentSelectionService<TKey>
    where TKey : notnull
{
    /// <summary>Raised after any mutation (<see cref="Add"/>, <see cref="Remove"/>, <see cref="AddRange"/>, <see cref="Clear"/>).</summary>
    event Action? SelectionChanged;

    /// <summary>Number of persisted keys.</summary>
    int Count { get; }

    /// <summary>Returns <c>true</c> if <paramref name="key"/> is currently persisted.</summary>
    bool Contains(TKey key);

    /// <summary>Adds <paramref name="key"/>. Idempotent — a no-op if already present. Returns <c>true</c> if the set changed.</summary>
    bool Add(TKey key);

    /// <summary>Adds every key in <paramref name="keys"/>. Returns the number of keys that were newly added.</summary>
    int AddRange(IEnumerable<TKey> keys);

    /// <summary>Removes <paramref name="key"/>. Returns <c>true</c> if the set changed.</summary>
    bool Remove(TKey key);

    /// <summary>Removes every key in <paramref name="keys"/>. Returns the number of keys that were actually removed.</summary>
    int RemoveRange(IEnumerable<TKey> keys);

    /// <summary>Clears the whole set. Returns <c>true</c> if the set was non-empty before the call.</summary>
    bool Clear();

    /// <summary>Immutable snapshot of the current set. Mutating the returned collection has no effect on the service.</summary>
    IReadOnlyCollection<TKey> Snapshot();
}
