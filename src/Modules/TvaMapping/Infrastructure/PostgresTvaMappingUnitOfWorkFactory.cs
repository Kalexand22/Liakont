namespace Liakont.Modules.TvaMapping.Infrastructure;

using Liakont.Modules.TvaMapping.Application;
using Stratum.Common.Infrastructure.Database;

internal sealed class PostgresTvaMappingUnitOfWorkFactory : ITvaMappingUnitOfWorkFactory
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresTvaMappingUnitOfWorkFactory(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ITvaMappingUnitOfWork> BeginAsync(CancellationToken ct = default)
    {
        return await PostgresTvaMappingUnitOfWork.BeginAsync(_connectionFactory, ct);
    }
}
