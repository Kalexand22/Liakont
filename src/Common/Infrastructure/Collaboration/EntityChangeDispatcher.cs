namespace Stratum.Common.Infrastructure.Collaboration;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Collaboration;

/// <summary>
/// Singleton dispatcher that bridges <see cref="ICircuitNotifier"/> (push side)
/// and <see cref="IEntityChangeSubscriber"/> (subscribe side).
/// Components subscribe with their circuit ID; when an entity change arrives,
/// the dispatcher invokes the callback for each targeted circuit.
/// </summary>
internal sealed partial class EntityChangeDispatcher : ICircuitNotifier, IEntityChangeSubscriber
{
    private readonly ConcurrentDictionary<string, Action<EntityChangedEvent>> _subscribers = new(StringComparer.Ordinal);
    private readonly ILogger<EntityChangeDispatcher> _logger;

    public EntityChangeDispatcher(ILogger<EntityChangeDispatcher> logger)
    {
        _logger = logger;
    }

    public void Subscribe(string circuitId, Action<EntityChangedEvent> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitId);
        ArgumentNullException.ThrowIfNull(callback);

        _subscribers[circuitId] = callback;
        LogSubscribed(_logger, circuitId);
    }

    public void Unsubscribe(string circuitId)
    {
        if (_subscribers.TryRemove(circuitId, out _))
        {
            LogUnsubscribed(_logger, circuitId);
        }
    }

    public Task NotifyEntityChangedAsync(EntityChangedEvent evt, IReadOnlyList<PresenceEntry> circuits)
    {
        foreach (var entry in circuits)
        {
            if (_subscribers.TryGetValue(entry.CircuitId, out var callback))
            {
                try
                {
                    callback(evt);
                }
                catch (Exception ex)
                {
                    LogCallbackFailed(_logger, entry.CircuitId, ex);
                }
            }
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Circuit {CircuitId} subscribed to entity change notifications")]
    private static partial void LogSubscribed(ILogger logger, string circuitId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Circuit {CircuitId} unsubscribed from entity change notifications")]
    private static partial void LogUnsubscribed(ILogger logger, string circuitId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Entity change callback failed for circuit {CircuitId}")]
    private static partial void LogCallbackFailed(ILogger logger, string circuitId, Exception exception);
}
