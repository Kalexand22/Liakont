namespace Stratum.Common.Abstractions.UiRules;

using System.Collections;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// An immutable set of dynamic UI attributes keyed by field name. Serializable to JSON
/// for transport to the Blazor client. Used by the UI rule engine, field-change
/// engine, and action pipeline to communicate UI state changes.
/// </summary>
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "UiAttributeSet is a domain-specific name, not a generic collection.")]
public sealed class UiAttributeSet : IReadOnlyDictionary<string, UiFieldAttributes>
{
    private readonly Dictionary<string, UiFieldAttributes> _attributes;

    public UiAttributeSet()
    {
        _attributes = new Dictionary<string, UiFieldAttributes>();
    }

    public UiAttributeSet(IEnumerable<KeyValuePair<string, UiFieldAttributes>> attributes)
    {
        _attributes = new Dictionary<string, UiFieldAttributes>(attributes);
    }

    public int Count => _attributes.Count;

    public IEnumerable<string> Keys => _attributes.Keys;

    public IEnumerable<UiFieldAttributes> Values => _attributes.Values;

    public UiFieldAttributes this[string key] => _attributes[key];

    /// <summary>
    /// Merges two attribute sets with property-level merge for overlapping fields.
    /// Non-default values from <paramref name="right"/> override values from <paramref name="left"/>.
    /// </summary>
    public static UiAttributeSet Merge(UiAttributeSet left, UiAttributeSet right)
    {
        var merged = new Dictionary<string, UiFieldAttributes>(left._attributes);

        foreach (var kvp in right._attributes)
        {
            merged[kvp.Key] = merged.TryGetValue(kvp.Key, out var existing)
                ? UiFieldAttributes.Merge(existing, kvp.Value)
                : kvp.Value;
        }

        return new UiAttributeSet(merged);
    }

    public bool ContainsKey(string key) => _attributes.ContainsKey(key);

    public bool TryGetValue(string key, out UiFieldAttributes value) =>
        _attributes.TryGetValue(key, out value!);

    public IEnumerator<KeyValuePair<string, UiFieldAttributes>> GetEnumerator() =>
        _attributes.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
