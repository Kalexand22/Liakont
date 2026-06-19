namespace Liakont.Modules.Mandats.Infrastructure;

using Liakont.Modules.Mandats.Application;
using Stratum.Common.Infrastructure.Database;

internal sealed class PostgresMandatsUnitOfWorkFactory : IMandatsUnitOfWorkFactory
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresMandatsUnitOfWorkFactory(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IMandatsUnitOfWork> BeginAsync(CancellationToken ct = default)
    {
        return await PostgresMandatsUnitOfWork.BeginAsync(_connectionFactory, ct);
    }
}
