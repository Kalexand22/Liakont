namespace Liakont.Agent.Adapters.EncheresV6.Tests.Fakes;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;

/// <summary>Collection de paramètres mockée (délègue à une liste interne).</summary>
internal sealed class FakeParameterCollection : IDataParameterCollection
{
    private readonly List<object?> _items = new List<object?>();

    public int Count => _items.Count;

    public bool IsFixedSize => false;

    public bool IsReadOnly => false;

    public bool IsSynchronized => false;

    public object SyncRoot { get; } = new object();

    public object? this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    public object this[string parameterName]
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public int Add(object? value)
    {
        _items.Add(value);
        return _items.Count - 1;
    }

    public void Clear() => _items.Clear();

    public bool Contains(object? value) => _items.Contains(value);

    public bool Contains(string parameterName) => throw new NotSupportedException();

    public void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    public IEnumerator GetEnumerator() => _items.GetEnumerator();

    public int IndexOf(object? value) => _items.IndexOf(value);

    public int IndexOf(string parameterName) => throw new NotSupportedException();

    public void Insert(int index, object? value) => _items.Insert(index, value);

    public void Remove(object? value) => _items.Remove(value);

    public void RemoveAt(int index) => _items.RemoveAt(index);

    public void RemoveAt(string parameterName) => throw new NotSupportedException();
}
