namespace Stratum.Common.UI.Services;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Default adapter that wires a typed <see cref="IPersistentSelectionService{TKey}"/>
/// to a <typeparamref name="TItem"/>-oriented grid via a key selector lambda.
/// <para>
/// The binding forwards every mutation to the underlying service and proxies
/// <see cref="IPersistentSelectionService{TKey}.SelectionChanged"/> as
/// <see cref="Changed"/> so the grid can subscribe without knowing
/// <typeparamref name="TKey"/>.
/// </para>
/// </summary>
public sealed class PersistentSelectionBinding<TItem, TKey> : IPersistentSelectionBinding<TItem>
    where TKey : notnull
{
    private readonly IPersistentSelectionService<TKey> _service;
    private readonly Func<TItem, TKey> _keySelector;

    public PersistentSelectionBinding(
        IPersistentSelectionService<TKey> service,
        Func<TItem, TKey> keySelector)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(keySelector);
        _service = service;
        _keySelector = keySelector;
    }

    /// <inheritdoc />
    public event Action? Changed
    {
        add => _service.SelectionChanged += value;
        remove => _service.SelectionChanged -= value;
    }

    /// <inheritdoc />
    public int TotalCount => _service.Count;

    /// <inheritdoc />
    public bool Contains(TItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _service.Contains(_keySelector(item));
    }

    /// <inheritdoc />
    public void Add(TItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _service.Add(_keySelector(item));
    }

    /// <inheritdoc />
    public int AddRange(IEnumerable<TItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return _service.AddRange(items.Select(_keySelector));
    }

    /// <inheritdoc />
    public int RemoveRange(IEnumerable<TItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return _service.RemoveRange(items.Select(_keySelector));
    }

    /// <inheritdoc />
    public void Clear() => _service.Clear();
}
