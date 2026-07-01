namespace Liakont.Modules.Ged.Tests.Integration.Fixtures;

using System;
using System.Reflection;
using DbUp;
using Liakont.Modules.Ged.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Conteneur PostgreSQL éphémère partagé. Chaque test obtient sa PROPRE base (une base = un tenant,
/// blueprint §7) via <see cref="CreateTenantDatabase"/> : le catalogue GED porte des contraintes UNIQUE
/// (codes d'axe / de type d'entité) et une table append-only (<c>catalog_change_log</c>, non réinitialisable),
/// donc une base isolée par test évite les collisions et les fuites d'état — même motif que
/// <c>ArchiveDatabaseFixture</c>. Les migrations sont appliquées dans l'ORDRE DbUp (nom de ressource) : le
/// socle Common puis les scripts GED (ged_catalog crée entity_types AVANT axis_definitions, RL-07).
/// </summary>
public sealed class GedDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync().AsTask();
    }

    /// <summary>
    /// Crée une base isolée (socle Common + migrations GED appliquées) et retourne sa fabrique de connexions
    /// (base TENANT — l'isolation EST la connexion, F19 §3.2). Le seul fait que les migrations GED s'appliquent
    /// sur une base VIERGE prouve l'ordre FK RL-07 : si axis_definitions précédait entity_types, la FK
    /// <c>fk_axis_def_target_entity</c> échouerait ici.
    /// </summary>
    public IConnectionFactory CreateTenantDatabase()
    {
        string databaseName = "tenant_" + Guid.NewGuid().ToString("N");
        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = databaseName };
        string connectionString = builder.ConnectionString;

        RunCommonMigrations(connectionString);
        RunGedMigrations(connectionString);
        return new NpgsqlConnectionFactory(Options.Create(new DatabaseOptions { ConnectionString = connectionString }));
    }

    private static void RunCommonMigrations(string connectionString)
    {
        // EnsureDatabase (dans MigrationRunner) crée la base si elle n'existe pas, puis applique le socle.
        var options = Options.Create(new DatabaseOptions { ConnectionString = connectionString });
        var migrationOptions = Options.Create(new MigrationAssembliesOptions());
        var runner = new MigrationRunner(options, migrationOptions, NullLogger<MigrationRunner>.Instance);
        runner.MigrateUp();
    }

    private static void RunGedMigrations(string connectionString)
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(GedModuleRegistration))!,
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
}
