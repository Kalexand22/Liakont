namespace Stratum.Common.Infrastructure.Outbox;

/// <summary>
/// Maps outbox event type strings to their CLR payload types for deserialization.
/// </summary>
public interface IEventTypeRegistry
{
    /// <summary>
    /// Returns the CLR payload type for the given event type string, or null if not registered.
    /// </summary>
    Type? GetPayloadType(string eventType);

    /// <summary>
    /// Registers a mapping from an event type string to a CLR payload type.
    /// </summary>
    IEventTypeRegistry Register<TPayload>(string eventType);
}
