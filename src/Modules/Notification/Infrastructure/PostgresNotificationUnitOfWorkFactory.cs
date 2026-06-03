namespace Stratum.Modules.Notification.Infrastructure;

using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Outbox;
using Stratum.Modules.Notification.Application;

internal sealed class PostgresNotificationUnitOfWorkFactory : INotificationUnitOfWorkFactory
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly IOutboxWriter _outboxWriter;

    public PostgresNotificationUnitOfWorkFactory(IConnectionFactory connectionFactory, IOutboxWriter outboxWriter)
    {
        _connectionFactory = connectionFactory;
        _outboxWriter = outboxWriter;
    }

    public async Task<INotificationUnitOfWork> BeginAsync(CancellationToken ct = default)
    {
        return await PostgresNotificationUnitOfWork.BeginAsync(_connectionFactory, _outboxWriter, ct);
    }
}
