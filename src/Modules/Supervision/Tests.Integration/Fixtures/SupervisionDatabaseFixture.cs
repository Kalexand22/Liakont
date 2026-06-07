namespace Liakont.Modules.Supervision.Tests.Integration.Fixtures;

using System;
using System.Reflection;
using DbUp;
using Liakont.Modules.Supervision.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Conteneur PostgreSQL éphémère partagé. Chaque test obtient sa PROPRE base (une base = un tenant,
/// blueprint §7) via <see cref="CreateTenantDatabase"/> : les schémas Common puis Supervision y sont
/// appliqués. Plusieurs bases du même conteneur permettent de prouver l'isolation cross-tenant du
/// dead-man's switch.
/// </summary>
public sealed class SupervisionDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync().AsTask();

    /// <summary>Crée une base isolée (Common + Supervision) et retourne (chaîne, fabrique de connexions).</summary>
    public TenantDatabase CreateTenantDatabase()
    {
        string databaseName = "tenant_" + Guid.NewGuid().ToString("N");
        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = databaseName };
        string connectionString = builder.ConnectionString;

        RunCommonMigrations(connectionString);
        RunModuleMigrations(connectionString, typeof(SupervisionModuleRegistration).Assembly);

        var factory = new NpgsqlConnectionFactory(Options.Create(new DatabaseOptions { ConnectionString = connectionString }));
        return new TenantDatabase(connectionString, factory);
    }

    private static void RunCommonMigrations(string connectionString)
    {
        var options = Options.Create(new DatabaseOptions { ConnectionString = connectionString });
        var migrationOptions = Options.Create(new MigrationAssembliesOptions());
        var runner = new MigrationRunner(options, migrationOptions, NullLogger<MigrationRunner>.Instance);
        runner.MigrateUp();
    }

    private static void RunModuleMigrations(string connectionString, Assembly assembly)
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(assembly, s => s.Contains(".Migrations.", StringComparison.Ordinal))
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
}
