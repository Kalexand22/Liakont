namespace Stratum.Common.Abstractions.Events;

/// <summary>
/// Dispatches integration events to registered handlers.
/// </summary>
public interface IEventDispatcher
{
    Task PublishAsync<TPayload>(IntegrationEvent<TPayload> integrationEvent, CancellationToken cancellationToken = default);
}
