namespace Liakont.Modules.Pipeline.Tests.Integration.Fixtures;

using System;
using System.Reflection;
using System.Threading.Tasks;
using DbUp;
using Liakont.Modules.Pipeline.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Conteneur PostgreSQL réel (Testcontainers) pour les tests d'intégration du module Pipeline : applique
/// les migrations communes (socle) puis les migrations du module (<c>pipeline.run_logs</c>), et expose
/// une <see cref="IConnectionFactory"/>. Même motif que <c>TvaMappingDatabaseFixture</c> /
/// <c>TenantSettingsDatabaseFixture</c>.
/// </summary>
public sealed class PipelineDatabaseFixture : IAsyncLifetime
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
        await _container.DisposeAsync();
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
                Assembly.GetAssembly(typeof(PipelineModuleRegistration))!,
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
