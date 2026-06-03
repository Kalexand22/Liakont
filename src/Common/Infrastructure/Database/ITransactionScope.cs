namespace Stratum.Common.Infrastructure.Database;

using System.Data;

public interface ITransactionScope : IAsyncDisposable
{
    IDbConnection Connection { get; }

    IDbTransaction Transaction { get; }

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}
