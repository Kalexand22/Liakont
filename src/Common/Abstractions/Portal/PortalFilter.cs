namespace Stratum.Common.Abstractions.Portal;

/// <summary>
/// Filter criteria for portal public event queries.
/// All fields are optional and combined with AND logic.
/// </summary>
public sealed record PortalFilter
{
    /// <summary>Inclusive start date.</summary>
    public DateTimeOffset? DateFrom { get; init; }

    /// <summary>Inclusive end date.</summary>
    public DateTimeOffset? DateTo { get; init; }

    /// <summary>Free-text keyword search across title and description.</summary>
    public string? Keyword { get; init; }

    /// <summary>Optional tenant filter — when set, only returns results from this tenant.</summary>
    public string? TenantId { get; init; }
}
