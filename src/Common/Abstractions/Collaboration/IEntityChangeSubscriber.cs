namespace Stratum.Common.Abstractions.Collaboration;

/// <summary>
/// Allows Blazor components to subscribe to entity-change notifications for a specific circuit.
/// Singleton service — shared across all circuits.
/// </summary>
public interface IEntityChangeSubscriber
{
    /// <summary>
    /// Register a callback that will be invoked when an entity change is detected
    /// for a circuit watching that entity.
    /// </summary>
    void Subscribe(string circuitId, Action<EntityChangedEvent> callback);

    /// <summary>
    /// Remove the subscription for the given circuit.
    /// </summary>
    void Unsubscribe(string circuitId);
}
