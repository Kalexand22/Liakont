namespace Stratum.Common.Infrastructure.Database;

using System.Data;
using Npgsql;

public sealed class TransactionScope : ITransactionScope
{
    private bool _completed;

    private TransactionScope(IDbConnection connection, IDbTransaction transaction)
    {
        Connection = connection;
        Transaction = transaction;
    }

    public IDbConnection Connection { get; }

    public IDbTransaction Transaction { get; }

    public static async Task<TransactionScope> BeginAsync(
        IConnectionFactory connectionFactory,
        CancellationToken cancellationToken = default)
    {
        var connection = await connectionFactory.OpenAsync(cancellationToken);
        var transaction = connection is NpgsqlConnection npgsql
            ? await npgsql.BeginTransactionAsync(cancellationToken)
            : connection.BeginTransaction();

        return new TransactionScope(connection, transaction);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_completed)
        {
            return;
        }

        if (Transaction is NpgsqlTransaction npgsqlTx)
        {
            await npgsqlTx.CommitAsync(cancellationToken);
        }
        else
        {
            Transaction.Commit();
        }

        _completed = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_completed)
        {
            return;
        }

        if (Transaction is NpgsqlTransaction npgsqlTx)
        {
            await npgsqlTx.RollbackAsync(cancellationToken);
        }
        else
        {
            Transaction.Rollback();
        }

        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            await RollbackAsync();
        }

        if (Transaction is IAsyncDisposable asyncTx)
        {
            await asyncTx.DisposeAsync();
        }
        else
        {
            Transaction.Dispose();
        }

        if (Connection is IAsyncDisposable asyncConn)
        {
            await asyncConn.DisposeAsync();
        }
        else
        {
            Connection.Dispose();
        }
    }
}
