namespace Stratum.Modules.Job.Infrastructure;

using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Job.Application;

internal sealed class PostgresScheduleUnitOfWorkFactory : IScheduleUnitOfWorkFactory
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresScheduleUnitOfWorkFactory(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IScheduleUnitOfWork> BeginAsync(CancellationToken ct = default)
    {
        return await PostgresScheduleUnitOfWork.BeginAsync(_connectionFactory, ct);
    }
}
