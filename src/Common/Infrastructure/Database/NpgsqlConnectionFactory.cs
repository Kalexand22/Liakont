namespace Stratum.Common.Infrastructure.Database;

using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

public sealed class NpgsqlConnectionFactory : IConnectionFactory, ISystemConnectionFactory
{
    private static readonly object InitLock = new();
    private static bool _initialized;

    private readonly string _connectionString;

    public NpgsqlConnectionFactory(IOptions<DatabaseOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
        EnsureTypeHandlers();
    }

    public async Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void EnsureTypeHandlers()
    {
        if (_initialized)
        {
            return;
        }

        lock (InitLock)
        {
            if (_initialized)
            {
                return;
            }

            SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
            _initialized = true;
        }
    }
}
