namespace Liakont.Modules.Signature.Tests.Integration.Fixtures;

using System.Reflection;
using Dapper;
using DbUp;
using Liakont.Modules.Signature.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Conteneur PostgreSQL avec DEUX bases tenant RÉELLES (<c>stratum_tenant_a</c>, <c>stratum_tenant_b</c>),
/// chacune migrée Common + module Signature (schéma <c>signature</c> : liaisons de signataire vérifié + journal
/// de preuve append-only). Prouve l'isolation cross-BASE (CLAUDE.md n°9) : une preuve / liaison écrite dans la
/// base d'un tenant n'est jamais visible dans la base de l'autre. Même patron que
/// <c>DocumentApprovalMultiTenantFixture</c>.
/// </summary>
public sealed class SignatureMultiTenantFixture : IAsyncLifetime
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
        RunCommonMigrations(SystemConnectionString);

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

    private static void RunCommonMigrations(string connectionString)
    {
        var options = Options.Create(new DatabaseOptions { ConnectionString = connectionString });
        var migrationOptions = Options.Create(new MigrationAssembliesOptions());
        var runner = new MigrationRunner(options, migrationOptions, NullLogger<MigrationRunner>.Instance);
        runner.MigrateUp();
    }

    private static void RunModuleMigrations(string connectionString)
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(SignatureModuleRegistration))!,
                s => s.Contains(".Migrations.", StringComparison.Ordinal))
            .JournalToPostgresqlTable("outbox", "schema_versions")
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            throw result.Error;
        }
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
        RunCommonMigrations(tenantConnStr);
        RunModuleMigrations(tenantConnStr);
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
