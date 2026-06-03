namespace Stratum.Common.Infrastructure.Database;

using System.Data;

public interface IConnectionFactory
{
    Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default);
}
