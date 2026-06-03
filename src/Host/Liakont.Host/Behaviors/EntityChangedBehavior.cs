namespace Liakont.Host.Behaviors;

using System.Collections.Concurrent;
using System.Reflection;
using MediatR;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Collaboration;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// MediatR pipeline behavior that broadcasts an <see cref="EntityChangedEvent"/>
/// to circuits watching the modified entity after a successful command execution.
/// Only triggers for commands decorated with <see cref="EntityChangeAttribute"/>.
/// Notification is fire-and-forget — it never blocks the command response.
/// </summary>
internal sealed partial class EntityChangedBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    // Cache attribute + property accessor per command type to avoid repeated reflection.
    private static readonly ConcurrentDictionary<Type, EntityChangeMetadata?> MetadataCache = new();

    private readonly ICollaborationService _collaboration;
    private readonly ICircuitNotifier _notifier;
    private readonly IActorContextAccessor _accessor;
    private readonly ILogger<EntityChangedBehavior<TRequest, TResponse>> _logger;

    public EntityChangedBehavior(
        ICollaborationService collaboration,
        ICircuitNotifier notifier,
        IActorContextAccessor accessor,
        ILogger<EntityChangedBehavior<TRequest, TResponse>> logger)
    {
        _collaboration = collaboration;
        _notifier = notifier;
        _accessor = accessor;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next();

        var metadata = MetadataCache.GetOrAdd(typeof(TRequest), ResolveMetadata);
        if (metadata is null)
        {
            return response;
        }

        var entityId = metadata.GetEntityId(request);
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return response;
        }

        var entityType = metadata.EntityType;
        var circuits = _collaboration.GetPresence(entityType, entityId);
        if (circuits.Count == 0)
        {
            return response;
        }

        var actor = _accessor.Current;
        var evt = new EntityChangedEvent(
            entityType,
            entityId,
            actor.DisplayName ?? actor.Email ?? actor.UserId.ToString(),
            DateTimeOffset.UtcNow);

        // Fire-and-forget: don't block the command response
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await _notifier.NotifyEntityChangedAsync(evt, circuits);
                }
                catch (Exception ex)
                {
                    LogNotificationFailed(_logger, entityType, entityId, ex);
                }
            },
            CancellationToken.None);

        LogEntityChangeBroadcast(_logger, entityType, entityId, circuits.Count);

        return response;
    }

    private static EntityChangeMetadata? ResolveMetadata(Type requestType)
    {
        var attr = requestType.GetCustomAttribute<EntityChangeAttribute>();
        if (attr is null)
        {
            return null;
        }

        var prop = requestType.GetProperty(attr.EntityIdProperty, BindingFlags.Public | BindingFlags.Instance);
        if (prop is null)
        {
            return null;
        }

        return new EntityChangeMetadata(attr.EntityType, prop);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Broadcasting EntityChangedEvent for {EntityType}:{EntityId} to {CircuitCount} circuit(s)")]
    private static partial void LogEntityChangeBroadcast(ILogger logger, string entityType, string entityId, int circuitCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to notify circuits for {EntityType}:{EntityId}")]
    private static partial void LogNotificationFailed(ILogger logger, string entityType, string entityId, Exception exception);

    private sealed class EntityChangeMetadata
    {
        private readonly PropertyInfo _entityIdProperty;

        public EntityChangeMetadata(string entityType, PropertyInfo entityIdProperty)
        {
            EntityType = entityType;
            _entityIdProperty = entityIdProperty;
        }

        public string EntityType { get; }

        public string? GetEntityId(object command) =>
            _entityIdProperty.GetValue(command)?.ToString();
    }
}
