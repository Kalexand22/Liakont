namespace Stratum.Common.UI.Models;

/// <summary>
/// Dry-run simulation request emitted by <see cref="Stratum.Common.UI.Components.RoutingPreviewConsole"/>.
/// The consumer maps this to a domain-level call (e.g. <c>IRoutingEngine.EvaluateRoutingAsync</c>).
/// </summary>
public record SimulationRequest
{
    /// <summary>Entity type to evaluate (e.g. "ReservationRequest", "WorkOrder").</summary>
    public required string EventType { get; init; }

    /// <summary>Raw JSON payload representing the entity data.</summary>
    public required string PayloadJson { get; init; }
}
