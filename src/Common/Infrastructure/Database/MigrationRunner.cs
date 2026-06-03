namespace Stratum.Common.Infrastructure.Database;

using System.Reflection;
using DbUp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

public sealed partial class MigrationRunner
{
    private readonly DatabaseOptions _options;

    private readonly MigrationAssembliesOptions _migrationOptions;

    private readonly ILogger<MigrationRunner> _logger;

    public MigrationRunner(
        IOptions<DatabaseOptions> options,
        IOptions<MigrationAssembliesOptions> migrationOptions,
        ILogger<MigrationRunner> logger)
    {
        _options = options.Value;
        _migrationOptions = migrationOptions.Value;
        _logger = logger;
    }

    public void MigrateUp()
    {
        EnsureDatabase.For.PostgresqlDatabase(_options.ConnectionString);
        EnsureJournalSchema();

        var builder = DeployChanges.To
            .PostgresqlDatabase(_options.ConnectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetExecutingAssembly(),
                s => s.Contains(".Migrations.", StringComparison.Ordinal));

        foreach (var assembly in _migrationOptions.Assemblies)
        {
            builder = builder.WithScriptsEmbeddedInAssembly(
                assembly,
                s => s.Contains(".Migrations.", StringComparison.Ordinal));
        }

        var upgrader = builder
            .JournalToPostgresqlTable("outbox", "schema_versions")
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            LogMigrationFailed(_logger, result.Error);
            throw result.Error;
        }

        LogMigrationSuccess(_logger);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Database migration failed")]
    private static partial void LogMigrationFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database migrations applied successfully")]
    private static partial void LogMigrationSuccess(ILogger logger);

    private void EnsureJournalSchema()
    {
        using var connection = new NpgsqlConnection(_options.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE SCHEMA IF NOT EXISTS outbox;";
        command.ExecuteNonQuery();
    }
}
