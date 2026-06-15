namespace Stratum.Common.Infrastructure.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DbUp;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Keycloak;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Prouve la levée du faux-vert RLF02 (finding F4 de la recette GATE_REALM_UNIQUE) : au démarrage,
/// <see cref="TenantProvisioningService.MigrateExistingTenantsAsync"/> ne doit PAS masquer un tenant
/// dont la base est injoignable derrière un résumé vert. Une base injoignable :
///   (1) n'est PAS comptée « migrée » (compteur honnête) ;
///   (2) est signalée par une alerte agrégée explicite (niveau Warning) qui la nomme ;
///   (3) n'interrompt PAS le démarrage (les tenants sains restent migrés) — seul un échec de
///       migration SQL (DbUp) lève. Cf. règle review #8 (faux-vert) et provenance §4.29.
/// xUnit instancie la classe (donc le conteneur) une fois par méthode : chaque test part d'une base
/// système vierge.
/// </summary>
public sealed class MigrateExistingTenantsInaccessibilityTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task MigrateExistingTenantsAsync_Should_NotCountInaccessibleTenant_And_RaiseAggregateWarning()
    {
        var connectionString = _container.GetConnectionString();
        EnsureOutboxSchema(connectionString);
        RunSystemMigrations(connectionString);

        // Tenant actif dont la base n'existe PAS (scénario exact de la recette : registre pointe vers
        // une base jamais créée). L'ouverture de connexion lèvera une PostgresException 3D000.
        await InsertActiveTenantAsync(connectionString, id: "ghost", databaseName: "stratum_ghost");

        var logger = new CapturingLogger();
        var service = CreateService(connectionString, logger);

        // Ne doit PAS lever : une base injoignable n'abandonne pas le démarrage.
        await service.MigrateExistingTenantsAsync();

        // (2) Alerte agrégée tracée au niveau Warning, nommant le tenant fautif.
        logger.Entries.Should().Contain(
            e => e.Level == LogLevel.Warning
                 && e.Message.Contains("inaccessible", StringComparison.OrdinalIgnoreCase)
                 && e.Message.Contains("ghost", StringComparison.Ordinal),
            "un tenant injoignable doit être signalé visiblement, pas masqué (faux-vert règle #8)");

        // (1) Compteur honnête : 0 migré, 1 injoignable — le tenant cassé n'est pas compté « migré ».
        logger.Entries.Should().Contain(
            e => e.Level == LogLevel.Information
                 && e.Message.Contains("0 migrated", StringComparison.Ordinal)
                 && e.Message.Contains("1 inaccessible", StringComparison.Ordinal),
            "le résumé de complétion doit refléter honnêtement 0 migré / 1 injoignable");
    }

    [Fact]
    public async Task MigrateExistingTenantsAsync_Should_CountHealthyTenant_AndStillFlagInaccessibleOne()
    {
        var connectionString = _container.GetConnectionString();
        EnsureOutboxSchema(connectionString);
        RunSystemMigrations(connectionString);

        // Tenant sain : sa base existe et se migre via le chemin de provisioning standard.
        await CreateDatabaseAsync(connectionString, "stratum_healthy");
        await InsertActiveTenantAsync(connectionString, id: "healthy", databaseName: "stratum_healthy");

        // Tenant cassé : base jamais créée.
        await InsertActiveTenantAsync(connectionString, id: "ghost", databaseName: "stratum_ghost");

        var logger = new CapturingLogger();
        var service = CreateService(connectionString, logger);

        await service.MigrateExistingTenantsAsync();

        // Le sain est migré ; le cassé n'est pas compté et reste signalé (compteur honnête mixte).
        logger.Entries.Should().Contain(
            e => e.Level == LogLevel.Information
                 && e.Message.Contains("1 migrated", StringComparison.Ordinal)
                 && e.Message.Contains("1 inaccessible", StringComparison.Ordinal),
            "le compteur distingue le tenant migré du tenant injoignable");

        logger.Entries.Should().Contain(
            e => e.Level == LogLevel.Warning
                 && e.Message.Contains("ghost", StringComparison.Ordinal)
                 && !e.Message.Contains("healthy", StringComparison.Ordinal),
            "l'alerte ne nomme que le tenant injoignable, pas le tenant sain");
    }

    private static TenantProvisioningService CreateService(string connectionString, ILogger<TenantProvisioningService> logger)
    {
        return new TenantProvisioningService(
            Options.Create(new DatabaseOptions { ConnectionString = connectionString }),
            Options.Create(new TenantConnectionOptions { DatabasePrefix = "stratum_" }),
            Options.Create(new MigrationAssembliesOptions()),
            new StubKeycloakRealmProvisioner(),
            new StubRealmRegistry(),
            Options.Create(new KeycloakAdminOptions { AdminBaseUrl = "http://localhost:8080" }),
            logger);
    }

    private static async Task InsertActiveTenantAsync(string connectionString, string id, string databaseName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO outbox.tenants (id, display_name, admin_email, database_name, realm_name, is_active)
            VALUES (@Id, @DisplayName, 'dev@liakont.local', @DatabaseName, @RealmName, true)
            """,
            new { Id = id, DisplayName = id, DatabaseName = databaseName, RealmName = $"liakont-{id}" });
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync($"CREATE DATABASE \"{databaseName}\"");
    }

    private static void RunSystemMigrations(string connectionString)
    {
        // On migre le schéma système jusqu'à V016 inclus : outbox.tenants existe avec database_name,
        // company_id reste nullable (on évite la garde NOT NULL/UNIQUE de V017 — non requise ici, le
        // chemin testé ne lit que id + database_name).
        var assembly = typeof(MigrationRunner).Assembly;
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                assembly,
                script => script.Contains(".Migrations.", StringComparison.Ordinal)
                          && MatchesVersionAtMost(script, 16))
            .JournalToPostgresqlTable("outbox", "schema_versions")
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build();

        upgrader.PerformUpgrade().Successful.Should().BeTrue("le schéma système doit se migrer jusqu'à V016");
    }

    private static bool MatchesVersionAtMost(string scriptName, int maxVersion)
    {
        var match = Regex.Match(scriptName, @"\.V(\d+)__");
        return match.Success && int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) <= maxVersion;
    }

    private static void EnsureOutboxSchema(string connectionString)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE SCHEMA IF NOT EXISTS outbox;";
        command.ExecuteNonQuery();
    }

    private sealed class CapturingLogger : ILogger<TenantProvisioningService>
    {
        public List<(LogLevel Level, EventId Id, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, eventId, formatter(state, exception)));
    }

    private sealed class StubKeycloakRealmProvisioner : IKeycloakRealmProvisioner
    {
        public Task<KeycloakProvisionResult> ProvisionRealmAsync(KeycloakRealmProvisionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(KeycloakProvisionResult.Created(request.RealmName, $"http://localhost:8080/realms/{request.RealmName}", request.ClientSecret));

        public Task DeleteRealmAsync(string realmName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AddTenantRedirectUriAsync(string primaryRealmName, string tenantSubdomain, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubRealmRegistry : IRealmRegistry
    {
        public bool IsKnownIssuer(string issuer) => false;

        public bool TryGetTenantId(string realmName, out string? tenantId)
        {
            tenantId = null;
            return false;
        }

        public void RegisterRealm(string realmName, string tenantId, string authority)
        {
        }

        public void UnregisterRealm(string realmName, string authority)
        {
        }
    }
}
