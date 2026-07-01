namespace Liakont.Modules.Ged.Infrastructure;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ged.Application;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Fabrique l'<see cref="IGedIndexUnitOfWork"/> Postgres à partir de l'<see cref="IConnectionFactory"/> ambiant
/// (tenant-scopé — F19 §3.2). Chaque <see cref="BeginAsync"/> ouvre une connexion + transaction fraîches.
/// </summary>
internal sealed class PostgresGedIndexUnitOfWorkFactory : IGedIndexUnitOfWorkFactory
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresGedIndexUnitOfWorkFactory(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IGedIndexUnitOfWork> BeginAsync(CancellationToken cancellationToken = default)
    {
        return await PostgresGedIndexUnitOfWork.BeginAsync(_connectionFactory, cancellationToken);
    }
}
