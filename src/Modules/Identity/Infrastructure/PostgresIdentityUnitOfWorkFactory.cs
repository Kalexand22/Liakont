namespace Stratum.Modules.Identity.Infrastructure;

using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Outbox;
using Stratum.Modules.Identity.Application;

public sealed class PostgresIdentityUnitOfWorkFactory : IIdentityUnitOfWorkFactory
{
    private readonly IConnectionFactory _connectionFactory;

    private readonly IOutboxWriter _outboxWriter;

    public PostgresIdentityUnitOfWorkFactory(IConnectionFactory connectionFactory, IOutboxWriter outboxWriter)
    {
        _connectionFactory = connectionFactory;
        _outboxWriter = outboxWriter;
    }

    public async Task<IIdentityUnitOfWork> BeginAsync(CancellationToken ct = default)
    {
        return await PostgresIdentityUnitOfWork.BeginAsync(_connectionFactory, _outboxWriter, ct);
    }
}
