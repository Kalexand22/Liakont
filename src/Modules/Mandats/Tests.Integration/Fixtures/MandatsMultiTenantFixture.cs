namespace Liakont.Modules.Mandats.Tests.Integration.Fixtures;

using System.Reflection;
using Dapper;
using DbUp;
using Liakont.Modules.Mandats.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Conteneur PostgreSQL avec une base SYSTÈME (catalogue <c>outbox.tenants</c>) et DEUX bases tenant réelles
/// (<c>stratum_tenant_a</c>, <c>stratum_tenant_b</c>), chacune migrée Common + module Mandats (schéma
/// <c>mandats</c> + journaux). Permet de prouver la bascule tacite (MND04) au travers du VRAI
/// <c>TenantJobRunner</c> (SOL06) sur ≥ 2 bases : balayage par tenant, isolation cross-base (les acceptations
/// d'un tenant ne sont jamais vues par l'autre).
/// </summary>
public sealed class MandatsMultiTenantFixture : IAsyncLifetime
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

        // Base système : catalogue des tenants (outbox.tenants) interrogé par TenantJobRunner.
        RunCommonMigrations(SystemConnectionString);

        await CreateTenantDatabaseAsync(TenantA);
        await CreateTenantDatabaseAsync(TenantB);

        await RegisterTenantAsync(TenantA, "Tenant A");
        await RegisterTenantAsync(TenantB, "Tenant B");
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync().AsTask();
    }

    public ITenantQueries CreateTenantQueries()
    {
        var options = Options.Create(new DatabaseOptions { ConnectionString = SystemConnectionString });
        return new TenantQueries(options);
    }

    public ITenantConnectionFactory CreateTenantConnectionFactory()
    {
        var registry = new NpgsqlDataSourceRegistry(NullLogger<NpgsqlDataSourceRegistry>.Instance);
        var dbOptions = Options.Create(new DatabaseOptions { ConnectionString = SystemConnectionString });
        var tenantOptions = Options.Create(new TenantConnectionOptions { DatabasePrefix = "stratum_" });
        return new TenantAwareNpgsqlConnectionFactory(
            dbOptions, tenantOptions, registry, NullLogger<TenantAwareNpgsqlConnectionFactory>.Instance);
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
                Assembly.GetAssembly(typeof(MandatsModuleRegistration))!,
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

    private async Task RegisterTenantAsync(string tenantId, string displayName)
    {
        await using var conn = new NpgsqlConnection(SystemConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO outbox.tenants (id, display_name, admin_email, database_name, realm_name, is_active, company_id)
            VALUES (@Id, @DisplayName, @Email, @DbName, @Realm, true, @CompanyId)
            ON CONFLICT (id) DO NOTHING
            """,
            new
            {
                Id = tenantId,
                DisplayName = displayName,
                Email = $"admin@{tenantId}.test",
                DbName = $"stratum_{tenantId.Replace('-', '_')}",
                Realm = tenantId,

                // company_id NOT NULL + UNIQUE (V017) : une valeur distincte par tenant suffit (les bascules
                // écrivent sous le company_id porté par la ligne d'acceptation, indépendant de ce catalogue).
                CompanyId = Guid.NewGuid(),
            });
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
