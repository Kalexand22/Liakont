namespace Liakont.Modules.Documents.Tests.Integration.Doubles;

using System.Data;
using Npgsql;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Double de test d'<see cref="ITenantConnectionFactory"/> : ouvre toujours la base UNIQUE du conteneur
/// de test, quel que soit le slug de tenant. Permet d'exercer le port d'ingestion (<c>DocumentIntake</c>),
/// qui résout sa connexion par SLUG, contre la base du conteneur.
/// </summary>
internal sealed class SingleDatabaseTenantConnectionFactory : ITenantConnectionFactory
{
    private readonly string _connectionString;

    public SingleDatabaseTenantConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IDbConnection> OpenAsync(string? tenantId, CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
