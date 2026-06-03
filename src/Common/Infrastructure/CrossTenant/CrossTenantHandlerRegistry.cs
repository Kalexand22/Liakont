namespace Stratum.Common.Infrastructure.CrossTenant;

using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.CrossTenant;

/// <summary>
/// Registry that resolves <see cref="ICrossTenantHandler{TPayload}"/> implementations by event type.
/// Populated at construction time by scanning DI-registered handler services.
/// Returns handlers that accept <see cref="JsonElement"/> payloads, automatically deserializing
/// to the handler's concrete <c>TPayload</c> type.
/// </summary>
public sealed partial class CrossTenantHandlerRegistry : ICrossTenantHandlerRegistry
{
    private readonly FrozenDictionary<string, ICrossTenantHandler<JsonElement>> _handlers;

    public CrossTenantHandlerRegistry(
        IEnumerable<HandlerRegistration> registrations,
        ILogger<CrossTenantHandlerRegistry> logger)
    {
        var dict = new Dictionary<string, ICrossTenantHandler<JsonElement>>(StringComparer.Ordinal);

        foreach (var registration in registrations)
        {
            var eventType = registration.EventType;
            var wrapper = registration.Wrapper;

            if (dict.TryGetValue(eventType, out _))
            {
                LogConflict(logger, eventType);
                continue; // first registration wins; conflict is logged as warning
            }

            dict[eventType] = wrapper;
            LogRegistered(logger, eventType);
        }

        _handlers = dict.ToFrozenDictionary(StringComparer.Ordinal);
        LogTotalRegistered(logger, _handlers.Count);
    }

    /// <inheritdoc />
    public ICrossTenantHandler<JsonElement>? Resolve(string eventType)
        => _handlers.GetValueOrDefault(eventType);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Duplicate cross-tenant handler for event type '{EventType}' - keeping first registration")]
    private static partial void LogConflict(ILogger logger, string eventType);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Registered cross-tenant handler for event type '{EventType}'")]
    private static partial void LogRegistered(ILogger logger, string eventType);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "CrossTenantHandlerRegistry initialized with {Count} handler(s)")]
    private static partial void LogTotalRegistered(ILogger logger, int count);

    /// <summary>
    /// Intermediate registration record used to pass handler metadata from DI to the registry.
    /// Each <see cref="HandlerRegistration"/> wraps a typed handler as a <see cref="ICrossTenantHandler{JsonElement}"/>.
    /// </summary>
    public sealed record HandlerRegistration(string EventType, ICrossTenantHandler<JsonElement> Wrapper);

    /// <summary>
    /// Adapter that wraps an <see cref="ICrossTenantHandler{TPayload}"/> into an
    /// <see cref="ICrossTenantHandler{JsonElement}"/>. Handles deserialization of the
    /// <see cref="JsonElement"/> payload to <typeparamref name="TPayload"/>.
    /// </summary>
    internal sealed class JsonElementAdapter<TPayload> : ICrossTenantHandler<JsonElement>
    {
        private readonly ICrossTenantHandler<TPayload> _inner;

        public JsonElementAdapter(ICrossTenantHandler<TPayload> inner)
        {
            _inner = inner;
        }

        public string EventType => _inner.EventType;

        public Task HandleAsync(CrossTenantEnvelope envelope, JsonElement payload, CancellationToken ct)
        {
            var typed = payload.Deserialize<TPayload>(CrossTenantJsonOptions.Instance)
                ?? throw new InvalidOperationException(
                    $"Failed to deserialize payload for event type '{EventType}' to {typeof(TPayload).Name}.");
            return _inner.HandleAsync(envelope, typed, ct);
        }
    }
}
