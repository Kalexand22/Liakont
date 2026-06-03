namespace Stratum.Common.Abstractions.Events;

/// <summary>
/// Handles an integration event with a specific payload type.
/// </summary>
/// <typeparam name="TPayload">The event payload type.</typeparam>
public interface IIntegrationEventConsumer<TPayload>
{
    Task HandleAsync(IntegrationEvent<TPayload> integrationEvent, CancellationToken cancellationToken = default);
}
