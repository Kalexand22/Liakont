namespace Stratum.Common.UI.Models;

/// <summary>
/// Lightweight UI projection of a resource allocation row consumed by
/// <c>ResourceAllocationTable</c> (UIC06). The component does NOT take domain
/// DTOs from the Resource module — consumers project their own allocation data
/// into this record at the call-site (encapsulation rule R1).
/// </summary>
public sealed record AllocationItem
{
    /// <summary>Stable identifier — used as the Blazor @key when rendering rows.</summary>
    public required string Id { get; init; }

    /// <summary>Display name of the allocated resource (e.g. "Bollards Mobiles (Zone A)").</summary>
    public required string Resource { get; init; }

    /// <summary>Resource type label (e.g. "Matériel Urbain", "Ressource Humaine", "Logistique").</summary>
    public required string Type { get; init; }

    /// <summary>Pre-formatted quantity (e.g. "24 units", "1 Team (4p)").</summary>
    public required string Quantity { get; init; }

    /// <summary>Pre-formatted time period (e.g. "24/08/2024 08:00 – 18:00").</summary>
    public required string Period { get; init; }

    /// <summary>Availability status of this allocation.</summary>
    public required AllocationItemStatus Status { get; init; }

    /// <summary>Optional conflict reference (e.g. "REF-442") when Status is Conflict.</summary>
    public string? ConflictRef { get; init; }

    /// <summary>Optional TTL expiry label (e.g. "48h", "23h restantes") for provisional allocations.</summary>
    public string? Ttl { get; init; }
}
