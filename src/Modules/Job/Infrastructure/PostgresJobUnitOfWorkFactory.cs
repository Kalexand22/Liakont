namespace Stratum.Modules.Job.Infrastructure;

using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Outbox;
using Stratum.Modules.Job.Application;

internal sealed class PostgresJobUnitOfWorkFactory : IJobUnitOfWorkFactory
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly IOutboxWriter _outboxWriter;

    public PostgresJobUnitOfWorkFactory(
        IConnectionFactory connectionFactory,
        IOutboxWriter outboxWriter)
    {
        _connectionFactory = connectionFactory;
        _outboxWriter = outboxWriter;
    }

    public async Task<IJobUnitOfWork> BeginAsync(CancellationToken ct = default)
    {
        return await PostgresJobUnitOfWork.BeginAsync(_connectionFactory, _outboxWriter, ct);
    }
}
