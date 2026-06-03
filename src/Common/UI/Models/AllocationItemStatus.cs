namespace Stratum.Common.UI.Models;

/// <summary>
/// Availability status of a resource allocation row in
/// <c>ResourceAllocationTable</c> (UIC06). Consumers map their domain
/// <c>AllocationStatus</c> to this UI enum at the call-site — the component
/// never references the Resource module domain directly (encapsulation rule R1).
/// </summary>
public enum AllocationItemStatus
{
    /// <summary>Resource is provisionally held — option pending confirmation.</summary>
    Provisional,

    /// <summary>Allocation confirmed and reserved.</summary>
    Validated,

    /// <summary>Scheduling conflict detected — requires resolution.</summary>
    Conflict,
}
