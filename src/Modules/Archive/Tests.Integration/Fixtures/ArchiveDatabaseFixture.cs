namespace Liakont.Modules.Archive.Tests.Integration.Fixtures;

using System.Reflection;
using DbUp;
using Liakont.Modules.Documents.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Conteneur PostgreSQL éphémère partagé. Chaque test obtient sa PROPRE base (une base = un tenant,
/// blueprint §7) via <see cref="CreateTenantDatabase"/> : c'est indispensable ici car
/// <c>documents.archive_entries</c> est WORM (aucun DELETE possible pour réinitialiser une base entre
/// tests) et la chaîne de hashes est globale au tenant — une base partagée mêlerait les chaînes des tests.
/// </summary>
public sealed class ArchiveDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync().AsTask();
    }

    /// <summary>Crée une base isolée (Common + migrations Documents appliquées) et retourne sa fabrique de connexions.</summary>
    public IConnectionFactory CreateTenantDatabase()
    {
        string databaseName = "tenant_" + Guid.NewGuid().ToString("N");
        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = databaseName };
        string connectionString = builder.ConnectionString;

        RunCommonMigrations(connectionString);
        RunDocumentsMigrations(connectionString);
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

    private static void RunDocumentsMigrations(string connectionString)
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(DocumentsModuleRegistration))!,
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
