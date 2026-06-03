namespace Stratum.Common.Abstractions.UiRules;

/// <summary>
/// Dynamic UI attributes for a single field, evaluated at runtime by the
/// UI rule engine. Serializable to JSON for transport to the Blazor client.
/// </summary>
public sealed record UiFieldAttributes
{
    public bool Hidden { get; init; }

    public bool ReadOnly { get; init; }

    public bool Required { get; init; }

    /// <summary>
    /// Optional domain filter expression to restrict lookup/select values
    /// (e.g., "status == 'Active'").
    /// </summary>
    public string? DomainFilter { get; init; }

    /// <summary>
    /// Merges two attribute records using OR-accumulation for boolean flags:
    /// once a flag is <c>true</c>, it stays <c>true</c>. This is intentional —
    /// a more restrictive rule cannot be overridden by a less restrictive one.
    /// <see cref="DomainFilter"/> uses last-write-wins (right overrides left when non-null).
    /// </summary>
    public static UiFieldAttributes Merge(UiFieldAttributes left, UiFieldAttributes right) =>
        new()
        {
            Hidden = right.Hidden || left.Hidden,
            ReadOnly = right.ReadOnly || left.ReadOnly,
            Required = right.Required || left.Required,
            DomainFilter = right.DomainFilter ?? left.DomainFilter,
        };
}
