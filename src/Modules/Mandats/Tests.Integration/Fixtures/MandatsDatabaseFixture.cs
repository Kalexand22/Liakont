namespace Liakont.Modules.Mandats.Tests.Integration.Fixtures;

using System.Reflection;
using DbUp;
using Liakont.Modules.DocumentApproval.Infrastructure;
using Liakont.Modules.Mandats.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Conteneur PostgreSQL éphémère (Testcontainers) + application des migrations Common puis du module
/// Mandats. Partagé par la collection de tests d'intégration.
/// </summary>
public sealed class MandatsDatabaseFixture : IAsyncLifetime
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
        // DocumentApproval AVANT Mandats : la migration de bascule Mandats (SIG05) écrit dans le schéma
        // documentapproval — ses tables (V001-V004) doivent exister d'abord. DocumentApproval est déclaré en
        // premier ici, et « ...DocumentApproval...V004 » trie de toute façon avant « ...Mandats...V010 » par nom
        // de ressource embarquée — la même garantie qu'en production (cf. AppBootstrap).
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(ConnectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(DocumentApprovalModuleRegistration))!,
                s => s.Contains(".Migrations.", StringComparison.Ordinal))
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
}
