namespace Liakont.Modules.Archive.Tests.Integration.Fixtures;

using System.Reflection;
using DbUp;
using DotNet.Testcontainers.Containers;
using Liakont.Modules.Documents.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Conteneur PostgreSQL éphémère DÉDIÉ à la preuve de sauvegarde/restauration de l'appliance (OPS01b).
/// Distinct de <see cref="ArchiveDatabaseFixture"/> car la preuve exige des capacités que celui-ci n'offre
/// pas : créer PLUSIEURS bases nommées (source + cible de restauration vierge) sur le même cluster, et
/// exécuter <c>pg_dump</c>/<c>pg_restore</c>/<c>createdb</c> DANS le conteneur (ce sont les outils que les
/// scripts <c>deploy/docker/backup.sh</c> / <c>restore.sh</c> invoquent réellement — on teste le vrai
/// mécanisme, pas une copie ligne-à-ligne maison). Le bootstrap de migrations reflète celui de
/// <see cref="ArchiveDatabaseFixture"/>.
/// </summary>
public sealed class BackupRestoreDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    /// <summary>Nom de connexion du super-utilisateur du conteneur (pour <c>pg_dump -U …</c> en socket local = trust).</summary>
    public string SuperUser => new NpgsqlConnectionStringBuilder(_container.GetConnectionString()).Username!;

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Crée une base nommée, applique le socle + les migrations Documents, retourne sa fabrique de connexions.</summary>
    public IConnectionFactory CreateMigratedDatabase(string databaseName)
    {
        string connectionString = ConnectionStringFor(databaseName);
        RunCommonMigrations(connectionString);
        RunDocumentsMigrations(connectionString);
        return new NpgsqlConnectionFactory(Options.Create(new DatabaseOptions { ConnectionString = connectionString }));
    }

    /// <summary>Crée une base VIERGE (aucun schéma) : cible d'une restauration <c>pg_restore</c> d'un dump complet.</summary>
    public async Task CreateEmptyDatabaseAsync(string databaseName)
    {
        ExecResult result = await _container.ExecAsync(new[] { "createdb", "-U", SuperUser, databaseName });
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"createdb {databaseName} a échoué ({result.ExitCode}) : {result.Stderr}");
        }
    }

    /// <summary>Fabrique de connexions vers une base existante (sans (re)migrer).</summary>
    public IConnectionFactory ConnectionFactoryFor(string databaseName) =>
        new NpgsqlConnectionFactory(Options.Create(new DatabaseOptions { ConnectionString = ConnectionStringFor(databaseName) }));

    /// <summary>Exécute une commande dans le conteneur (pg_dump / pg_restore) et exige un code de sortie 0.</summary>
    public async Task ExecOkAsync(params string[] command)
    {
        ExecResult result = await _container.ExecAsync(command);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Commande conteneur échouée ({result.ExitCode}) : {string.Join(' ', command)}{Environment.NewLine}{result.Stderr}");
        }
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

    private string ConnectionStringFor(string databaseName) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = databaseName }.ConnectionString;
}
