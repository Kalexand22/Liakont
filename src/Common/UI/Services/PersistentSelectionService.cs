namespace Stratum.Common.UI.Services;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Default in-memory implementation of <see cref="IPersistentSelectionService{TKey}"/>.
/// Backed by a <see cref="HashSet{T}"/>; one instance per Blazor circuit per
/// <typeparamref name="TKey"/> via open-generic scoped DI registration.
/// Thread-safety is provided by a lock — Blazor Server circuits are
/// single-threaded but SignalR invocations may race with background notifications.
/// </summary>
public sealed class PersistentSelectionService<TKey> : IPersistentSelectionService<TKey>
    where TKey : notnull
{
    private readonly HashSet<TKey> _keys = new();
    private readonly Lock _lock = new();

    /// <inheritdoc />
    public event Action? SelectionChanged;

    /// <inheritdoc />
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _keys.Count;
            }
        }
    }

    /// <inheritdoc />
    public bool Contains(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_lock)
        {
            return _keys.Contains(key);
        }
    }

    /// <inheritdoc />
    public bool Add(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        bool added;
        lock (_lock)
        {
            added = _keys.Add(key);
        }

        if (added)
        {
            SelectionChanged?.Invoke();
        }

        return added;
    }

    /// <inheritdoc />
    public int AddRange(IEnumerable<TKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        // Materialize outside the lock so a slow caller-supplied key selector
        // (via PersistentSelectionBinding) can't serialize other circuit calls
        // or deadlock on the non-recursive System.Threading.Lock.
        var snapshot = keys.ToArray();
        var added = 0;
        lock (_lock)
        {
            foreach (var key in snapshot)
            {
                if (_keys.Add(key))
                {
                    added++;
                }
            }
        }

        if (added > 0)
        {
            SelectionChanged?.Invoke();
        }

        return added;
    }

    /// <inheritdoc />
    public bool Remove(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        bool removed;
        lock (_lock)
        {
            removed = _keys.Remove(key);
        }

        if (removed)
        {
            SelectionChanged?.Invoke();
        }

        return removed;
    }

    /// <inheritdoc />
    public int RemoveRange(IEnumerable<TKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var snapshot = keys.ToArray();
        var removed = 0;
        lock (_lock)
        {
            foreach (var key in snapshot)
            {
                if (_keys.Remove(key))
                {
                    removed++;
                }
            }
        }

        if (removed > 0)
        {
            SelectionChanged?.Invoke();
        }

        return removed;
    }

    /// <inheritdoc />
    public bool Clear()
    {
        bool wasNotEmpty;
        lock (_lock)
        {
            wasNotEmpty = _keys.Count > 0;
            _keys.Clear();
        }

        if (wasNotEmpty)
        {
            SelectionChanged?.Invoke();
        }

        return wasNotEmpty;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<TKey> Snapshot()
    {
        lock (_lock)
        {
            return _keys.ToArray();
        }
    }
}
