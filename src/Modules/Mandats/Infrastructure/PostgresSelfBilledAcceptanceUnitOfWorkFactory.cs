namespace Liakont.Modules.Mandats.Infrastructure;

using Liakont.Modules.Mandats.Application;
using Stratum.Common.Infrastructure.Database;

internal sealed class PostgresSelfBilledAcceptanceUnitOfWorkFactory : ISelfBilledAcceptanceUnitOfWorkFactory
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresSelfBilledAcceptanceUnitOfWorkFactory(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ISelfBilledAcceptanceUnitOfWork> BeginAsync(CancellationToken ct = default)
    {
        return await PostgresSelfBilledAcceptanceUnitOfWork.BeginAsync(_connectionFactory, ct);
    }
}
