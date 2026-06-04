namespace Liakont.Modules.Ingestion.Infrastructure;

using Liakont.Modules.Ingestion.Application;
using Stratum.Common.Infrastructure.Database;

internal sealed class PostgresAgentRegistryUnitOfWorkFactory : IAgentRegistryUnitOfWorkFactory
{
    private readonly ISystemConnectionFactory _systemConnectionFactory;

    public PostgresAgentRegistryUnitOfWorkFactory(ISystemConnectionFactory systemConnectionFactory)
    {
        _systemConnectionFactory = systemConnectionFactory;
    }

    public async Task<IAgentRegistryUnitOfWork> BeginAsync(CancellationToken cancellationToken = default)
    {
        return await PostgresAgentRegistryUnitOfWork.BeginAsync(_systemConnectionFactory, cancellationToken);
    }
}
