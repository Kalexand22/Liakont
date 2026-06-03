namespace Stratum.Common.Infrastructure.Events;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Events;

/// <summary>
/// In-process event dispatcher that resolves handlers from the DI container.
/// </summary>
public sealed partial class InMemoryEventDispatcher : IEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InMemoryEventDispatcher> _logger;

    public InMemoryEventDispatcher(IServiceProvider serviceProvider, ILogger<InMemoryEventDispatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task PublishAsync<TPayload>(
        IntegrationEvent<TPayload> integrationEvent,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var consumers = scope.ServiceProvider.GetServices<IIntegrationEventConsumer<TPayload>>();

        foreach (var consumer in consumers)
        {
            LogDispatching(_logger, integrationEvent.EventType, integrationEvent.EventId, consumer.GetType().Name);
            await consumer.HandleAsync(integrationEvent, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dispatching event {EventType} ({EventId}) to {ConsumerType}")]
    private static partial void LogDispatching(ILogger logger, string eventType, Guid eventId, string consumerType);
}
