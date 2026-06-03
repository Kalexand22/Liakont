namespace Stratum.Modules.Identity.Tests.Integration.Fixtures;

using System.Reflection;
using DbUp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

public sealed class IdentityDatabaseFixture : IAsyncLifetime
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
                Assembly.GetAssembly(typeof(Infrastructure.IdentityModuleRegistration))!,
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
