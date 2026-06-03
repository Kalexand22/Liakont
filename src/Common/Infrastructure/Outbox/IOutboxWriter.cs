namespace Stratum.Common.Infrastructure.Outbox;

using Stratum.Common.Abstractions.Events;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Writes integration events to the outbox table within an existing transaction.
/// </summary>
public interface IOutboxWriter
{
    Task WriteAsync<TPayload>(
        ITransactionScope transaction,
        IntegrationEvent<TPayload> integrationEvent,
        CancellationToken cancellationToken = default);
}
