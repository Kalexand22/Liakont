namespace Stratum.Common.Infrastructure.Outbox;

using System.Collections.Concurrent;

/// <summary>
/// Thread-safe registry mapping event type strings to CLR payload types.
/// </summary>
public sealed class EventTypeRegistry : IEventTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _types = new(StringComparer.OrdinalIgnoreCase);

    public Type? GetPayloadType(string eventType)
    {
        return _types.GetValueOrDefault(eventType);
    }

    public IEventTypeRegistry Register<TPayload>(string eventType)
    {
        _types[eventType] = typeof(TPayload);
        return this;
    }
}
