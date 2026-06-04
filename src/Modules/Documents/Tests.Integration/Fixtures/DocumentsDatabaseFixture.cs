namespace Liakont.Modules.Documents.Tests.Integration.Fixtures;

using System.Reflection;
using DbUp;
using Liakont.Modules.Documents.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Conteneur PostgreSQL éphémère (Testcontainers) + application des migrations Common puis du module
/// Documents. Partagé par la collection de tests d'intégration. La base unique du conteneur représente
/// la base DU TENANT (la connexion = le tenant — database-per-tenant, blueprint §7).
/// </summary>
public sealed class DocumentsDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        RunCommonMigrations();
        RunModuleMigrations();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync().AsTask();
    }

    public IConnectionFactory CreateConnectionFactory()
    {
        var options = Options.Create(new DatabaseOptions { ConnectionString = ConnectionString });
        return new NpgsqlConnectionFactory(options);
    }

    private void RunCommonMigrations()
    {
        var options = Options.Create(new DatabaseOptions { ConnectionString = ConnectionString });
        var migrationOptions = Options.Create(new MigrationAssembliesOptions());
        var runner = new MigrationRunner(options, migrationOptions, NullLogger<MigrationRunner>.Instance);
        runner.MigrateUp();
    }

    private void RunModuleMigrations()
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(ConnectionString)
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
