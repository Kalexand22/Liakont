namespace Stratum.Common.Infrastructure.Tests.Integration.Portal;

using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Creates a PostgreSQL container with a system DB and 2 tenant databases,
/// each with party + config schemas. Registers tenants in outbox.tenants.
/// </summary>
public sealed class MultiTenantFixture : IAsyncLifetime
{
    public const string TenantA = "tenant-a";
    public const string TenantB = "tenant-b";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string SystemConnectionString => _container.GetConnectionString();

    public static void RunMigrations(string connectionString)
    {
        var options = Options.Create(new DatabaseOptions { ConnectionString = connectionString });
        var migrationOptions = Options.Create(new MigrationAssembliesOptions());
        var runner = new MigrationRunner(options, migrationOptions, NullLogger<MigrationRunner>.Instance);
        runner.MigrateUp();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        RunMigrations(SystemConnectionString);

        await CreateTenantDatabaseAsync(TenantA);
        await CreateTenantDatabaseAsync(TenantB);

        await RegisterTenantAsync(TenantA, "Tenant A Assoc");
        await RegisterTenantAsync(TenantB, "Tenant B Muni");
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
        var registry = new NpgsqlDataSourceRegistry(
            NullLogger<NpgsqlDataSourceRegistry>.Instance);
        var dbOptions = Options.Create(new DatabaseOptions { ConnectionString = SystemConnectionString });
        var tenantOptions = Options.Create(new TenantConnectionOptions { DatabasePrefix = "stratum_" });
        return new TenantAwareNpgsqlConnectionFactory(
            dbOptions,
            tenantOptions,
            registry,
            NullLogger<TenantAwareNpgsqlConnectionFactory>.Instance);
    }

    public async Task SeedPublicPartyAsync(
        string tenantId,
        string legalName,
        bool isPublic,
        bool portalEnabled)
    {
        var connStr = BuildTenantConnectionString(tenantId);
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        if (portalEnabled)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO config.settings (key, value, value_type, module_source)
                VALUES ('feature.portal.enabled', 'true', 'bool', 'portal')
                ON CONFLICT (key) DO UPDATE SET value = 'true'
                """);
        }

        await conn.ExecuteAsync(
            """
            INSERT INTO party.parties (legal_name, is_public, is_active)
            VALUES (@LegalName, @IsPublic, true)
            """,
            new { LegalName = legalName, IsPublic = isPublic });
    }

    private static async Task CreateTenantSchemasAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            """
            CREATE SCHEMA IF NOT EXISTS party;
            CREATE SCHEMA IF NOT EXISTS config;

            CREATE TABLE IF NOT EXISTS party.parties (
                id            uuid          NOT NULL DEFAULT gen_random_uuid(),
                legal_name    text          NOT NULL,
                trade_name    text,
                party_type    text          NOT NULL DEFAULT 'Organization',
                tax_id        text,
                notes         text,
                is_active     boolean       NOT NULL DEFAULT true,
                is_public     boolean       NOT NULL DEFAULT false,
                created_at    timestamptz   NOT NULL DEFAULT now(),
                updated_at    timestamptz,
                CONSTRAINT pk_parties PRIMARY KEY (id)
            );

            CREATE INDEX IF NOT EXISTS ix_parties_is_public
                ON party.parties (is_public) WHERE is_public = true;

            CREATE TABLE IF NOT EXISTS config.settings (
                id            uuid          NOT NULL DEFAULT gen_random_uuid(),
                key           text          NOT NULL,
                value         text          NOT NULL,
                value_type    text          NOT NULL,
                description   text,
                module_source text,
                created_at    timestamptz   NOT NULL DEFAULT now(),
                updated_at    timestamptz,
                CONSTRAINT pk_settings PRIMARY KEY (id),
                CONSTRAINT uq_settings_key UNIQUE (key)
            );
            """);
    }

    private async Task CreateTenantDatabaseAsync(string tenantId)
    {
        var dbName = $"stratum_{tenantId.Replace('-', '_')}";

        await using var conn = new NpgsqlConnection(SystemConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync($"CREATE DATABASE \"{dbName}\"");

        var tenantConnStr = BuildTenantConnectionString(tenantId);
        await CreateTenantSchemasAsync(tenantConnStr);
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

                // company_id est NOT NULL + UNIQUE depuis V017 : une valeur distincte par tenant suffit
                // ici (ces tests portail valident la mécanique cross-tenant au niveau infra, hors chaîne
                // de résolution du Host).
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
