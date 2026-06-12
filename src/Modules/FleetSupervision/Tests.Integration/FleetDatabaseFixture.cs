namespace Liakont.Modules.FleetSupervision.Tests.Integration;

using System.Threading.Tasks;
using Liakont.Modules.FleetSupervision.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Base PostgreSQL réelle (Testcontainers) migrée avec l'assembly du module FleetSupervision (OPS04) :
/// applique le schéma <c>fleet</c> et la table <c>fleet.instances</c> dans la base SYSTÈME, comme le
/// <c>MigrationRunner</c> au démarrage. Le store cible cette base via <see cref="NpgsqlConnectionFactory"/>
/// (qui implémente <c>ISystemConnectionFactory</c>).
/// </summary>
public sealed class FleetDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        RunMigrations();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync().AsTask();
    }

    public NpgsqlConnectionFactory CreateConnectionFactory()
    {
        var options = Options.Create(new DatabaseOptions { ConnectionString = ConnectionString });
        return new NpgsqlConnectionFactory(options);
    }

    private void RunMigrations()
    {
        var options = Options.Create(new DatabaseOptions { ConnectionString = ConnectionString });
        var migrationOptions = Options.Create(new MigrationAssembliesOptions());
        migrationOptions.Value.Add(typeof(PostgresFleetStore).Assembly);
        var runner = new MigrationRunner(options, migrationOptions, NullLogger<MigrationRunner>.Instance);
        runner.MigrateUp();
    }
}
