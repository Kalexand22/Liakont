namespace Liakont.Modules.DocumentApproval.Tests.Integration.Fixtures;

using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Conteneur PostgreSQL avec DEUX bases tenant RÉELLES (<c>stratum_tenant_a</c>, <c>stratum_tenant_b</c>),
/// chacune migrée Common + module DocumentApproval. Prouve l'isolation cross-BASE (CLAUDE.md n°9) : une
/// validation écrite dans la base d'un tenant n'est jamais visible dans la base de l'autre.
/// </summary>
public sealed class DocumentApprovalMultiTenantFixture : IAsyncLifetime
{
    public const string TenantA = "tenant-a";
    public const string TenantB = "tenant-b";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string SystemConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        DocumentApprovalDatabaseFixture.RunCommonMigrations(SystemConnectionString);

        await CreateTenantDatabaseAsync(TenantA);
        await CreateTenantDatabaseAsync(TenantB);
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync().AsTask();
    }

    /// <summary>Fabrique de connexion DIRECTE vers la base d'un tenant (semis + assertions de test).</summary>
    public IConnectionFactory CreateConnectionFactory(string tenantId)
    {
        var options = Options.Create(new DatabaseOptions { ConnectionString = BuildTenantConnectionString(tenantId) });
        return new NpgsqlConnectionFactory(options);
    }

    private async Task CreateTenantDatabaseAsync(string tenantId)
    {
        var dbName = $"stratum_{tenantId.Replace('-', '_')}";

        await using (var conn = new NpgsqlConnection(SystemConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync($"CREATE DATABASE \"{dbName}\"");
        }

        var tenantConnStr = BuildTenantConnectionString(tenantId);
        DocumentApprovalDatabaseFixture.RunCommonMigrations(tenantConnStr);
        DocumentApprovalDatabaseFixture.RunModuleMigrations(tenantConnStr);
    }

    private string BuildTenantConnectionString(string tenantId)
    {
        var builder = new NpgsqlConnectionStringBuilder(SystemConnectionString)
        {
            Database = $"stratum_{tenantId.Replace('-', '_')}",
        };
        return builder.ToString();
    }
}
