namespace Stratum.Common.UI.Models;

/// <summary>
/// Lightweight UI projection of an availability conflict consumed by
/// <c>ConflictAlertBanner</c> (UIC03). The component intentionally does NOT
/// take a domain DTO from the Resource module — that would couple
/// <c>Stratum.Common.UI</c> to a feature module and break encapsulation rule
/// R1 of the UI architecture (no cross-module references in Common.UI).
///
/// Consumers (public assistant, agent detail, blocks page) map their own
/// <c>ConflictDto</c> / <c>ConflictingOccurrenceDto</c> into this record at
/// the call-site.
/// </summary>
public sealed record ConflictItem
{
    /// <summary>Stable identifier — used as the React-style key when rendering the list.</summary>
    public required string Id { get; init; }

    /// <summary>Display name of the contested resource (e.g. "Place du Marché").</summary>
    public required string Resource { get; init; }

    /// <summary>Pre-formatted time window (e.g. "24/08/2024 08:00 – 18:00").
    /// The component does not format dates so the consumer keeps full control
    /// over locale and timezone.</summary>
    public required string TimeWindow { get; init; }

    /// <summary>Short human-readable reason (e.g. "Marché hebdomadaire concurrent").</summary>
    public required string Reason { get; init; }

    /// <summary>Optional secondary detail line (e.g. capacity / required action).</summary>
    public string? Detail { get; init; }
}
