namespace Stratum.Common.Infrastructure.Database;

using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Dapper;
using DbUp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Keycloak;

/// <summary>
/// Provisions new tenants by creating their database, running all module migrations,
/// and registering them in the <c>outbox.tenants</c> table of the system database.
/// <para>
/// Idempotent: if the tenant already exists in the registry, returns success
/// without modifying anything.
/// </para>
/// <para>
/// On failure mid-provisioning, attempts rollback by dropping the partially-created
/// database and removing the registry entry.
/// </para>
/// </summary>
public sealed partial class TenantProvisioningService : ITenantProvisioningService
{
    private const int MaxPgIdentifierLength = 63;

    /// <summary>
    /// Infrastructure migration scripts that must NOT run in tenant databases.
    /// Only the tenant registry tables are system-only — everything else
    /// (outbox, audit, grid, events) is needed per-tenant.
    /// </summary>
    private static readonly HashSet<string> SystemOnlyMigrationPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "V008__", // tenants registry
        "V009__", // rename schema_name to database_name
        "V010__", // add realm_name to tenants
        "V011__", // cross-tenant events outbox
        "V016__", // add company_id to tenants registry
    };

    private readonly string _defaultConnectionString;
    private readonly string _databasePrefix;
    private readonly MigrationAssembliesOptions _migrationOptions;
    private readonly IKeycloakRealmProvisioner _keycloakProvisioner;
    private readonly IRealmRegistry _realmRegistry;
    private readonly KeycloakAdminOptions _keycloakOptions;
    private readonly ILogger<TenantProvisioningService> _logger;

    public TenantProvisioningService(
        IOptions<DatabaseOptions> databaseOptions,
        IOptions<TenantConnectionOptions> tenantOptions,
        IOptions<MigrationAssembliesOptions> migrationOptions,
        IKeycloakRealmProvisioner keycloakProvisioner,
        IRealmRegistry realmRegistry,
        IOptions<KeycloakAdminOptions> keycloakOptions,
        ILogger<TenantProvisioningService> logger)
    {
        _defaultConnectionString = databaseOptions.Value.ConnectionString;
        _databasePrefix = tenantOptions.Value.DatabasePrefix;
        _migrationOptions = migrationOptions.Value;
        _keycloakProvisioner = keycloakProvisioner;
        _realmRegistry = realmRegistry;
        _keycloakOptions = keycloakOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task MigrateExistingTenantsAsync(CancellationToken cancellationToken = default)
    {
        List<TenantRecord> tenantList;
        {
            await using var connection = new NpgsqlConnection(_defaultConnectionString);
            await connection.OpenAsync(cancellationToken);

            var tenants = await connection.QueryAsync<TenantRecord>(
                new CommandDefinition(
                    "SELECT id, database_name FROM outbox.tenants WHERE is_active = true",
                    cancellationToken: cancellationToken));

            tenantList = tenants.ToList();
        }

        if (tenantList.Count == 0)
        {
            LogNoTenantsToMigrate(_logger);
            return;
        }

        LogTenantMigrationStarted(_logger, tenantList.Count);

        var migrated = 0;
        var failures = new List<(string TenantId, Exception Error)>();

        foreach (var tenant in tenantList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await RunTenantMigrationsAsync(tenant.DatabaseName, cancellationToken);
                migrated++;
                LogTenantMigrated(_logger, tenant.Id, tenant.DatabaseName);
            }
            catch (NpgsqlException ex)
            {
                // Connection/network error — skip with warning (tenant DB inaccessible)
                LogTenantMigrationSkipped(_logger, tenant.Id, tenant.DatabaseName, ex);
            }
            catch (InvalidOperationException ex)
            {
                // Migration execution error (DbUp SQL failure) — log and collect
                LogTenantMigrationFailed(_logger, tenant.Id, tenant.DatabaseName, ex);
                failures.Add((tenant.Id, ex));
            }
        }

        LogTenantMigrationCompleted(_logger, migrated, tenantList.Count);

        if (failures.Count > 0)
        {
            throw new AggregateException(
                $"Tenant migration failed for {failures.Count} tenant(s): "
                + string.Join(", ", failures.Select(f => f.TenantId)),
                failures.Select(f => f.Error));
        }
    }

    public async Task<TenantProvisionResult> ProvisionAsync(
        TenantProvisionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TenantIdRegex().IsMatch(request.TenantId))
        {
            return TenantProvisionResult.Failed(
                $"Invalid tenant ID format: '{request.TenantId}'. "
                + "Must be 1-63 lowercase alphanumeric characters or hyphens, starting and ending with a letter or digit.");
        }

        var databaseName = $"{_databasePrefix}{request.TenantId.Replace('-', '_')}";

        if (databaseName.Length > MaxPgIdentifierLength)
        {
            return TenantProvisionResult.Failed(
                $"Composed database name '{databaseName}' exceeds PostgreSQL's {MaxPgIdentifierLength}-character identifier limit.");
        }

        var realmName = $"stratum-{request.TenantId}";
        var baseUrl = _keycloakOptions.AdminBaseUrl.TrimEnd('/');
        var authority = $"{baseUrl}/realms/{realmName}";

        LogProvisioningStarted(_logger, request.TenantId, databaseName);

        var databaseCreated = false;
        var realmCreated = false;
        try
        {
            // Check idempotency: tenant already in registry?
            if (await TenantExistsAsync(request.TenantId, cancellationToken))
            {
                LogTenantAlreadyProvisioned(_logger, request.TenantId);
                return TenantProvisionResult.Idempotent(databaseName, realmName, authority);
            }

            // Phase 1: Database provisioning
            await CreateDatabaseAsync(databaseName, cancellationToken);
            databaseCreated = true;

            await RunTenantMigrationsAsync(databaseName, cancellationToken);

            // No tenant admin is seeded here: the realm starts with NO user. The tenant's first
            // user is provisioned by the operator wizard (OPS03 lot A); the instance super-admin
            // is a cross-tenant actor of the PRIMARY realm and never lives inside a tenant realm.

            // Phase 2: Keycloak realm provisioning
            var clientSecret = GenerateClientSecret();

            // One tenant = one company: the company scope of every realm user is fixed at
            // provisioning time, persisted in the registry, and emitted as a hardcoded claim
            // by the realm's OIDC client. Seed import and user provisioning reuse this value.
            var companyId = Guid.NewGuid();

            if (_keycloakOptions.IsConfigured)
            {
                var appBaseUrl = _keycloakOptions.AppBaseUrl.TrimEnd('/');
                var keycloakRequest = new KeycloakRealmProvisionRequest
                {
                    TenantId = request.TenantId,
                    DisplayName = request.DisplayName,
                    RealmName = realmName,
                    ClientSecret = clientSecret,
                    CompanyId = companyId.ToString(),
                    RedirectUris = [$"{appBaseUrl}/*", $"{appBaseUrl.Replace("://", "://*.").TrimEnd('/')}/*"],
                    WebOrigins = [appBaseUrl, appBaseUrl.Replace("://", "://*.").TrimEnd('/')],
                };

                var kcResult = await _keycloakProvisioner.ProvisionRealmAsync(keycloakRequest, cancellationToken);
                if (!kcResult.Success)
                {
                    return TenantProvisionResult.Failed($"Keycloak provisioning failed: {kcResult.ErrorMessage}");
                }

                realmCreated = !kcResult.AlreadyProvisioned;

                // Shared SaaS realm seam (Liakont RLM04, ADR-0021 §1/§5) : le no-op du profil PARTAGÉ
                // ne provisionne aucun realm et renvoie une AUTORITÉ VIDE → ni enregistrement de realm
                // par tenant ni redirect par sous-domaine (code devenu vestigial en realm unique). Le
                // vrai provisioner (profil DÉDIÉ) renvoie l'autorité du realm — qu'il vienne d'être créé
                // (Created) OU qu'il préexiste (Idempotent, chemin de REPRISE) — et la mécanique d'origine
                // s'applique INCONDITIONNELLEMENT pour lui (ré-enregistrement idempotent du realm pour la
                // validation JWT, comme avant RLM04 — sinon une reprise laisserait le realm non enregistré).
                // Le redirect statique default.localhost (realm-export.json) n'est pas touché (FIX07a).
                if (!string.IsNullOrEmpty(kcResult.Authority))
                {
                    // Register realm for immediate JWT validation (no restart needed)
                    _realmRegistry.RegisterRealm(realmName, request.TenantId, authority);

                    // Add subdomain redirect URI to the primary realm so browser login works
                    if (!string.IsNullOrEmpty(_keycloakOptions.PrimaryRealmName))
                    {
                        await _keycloakProvisioner.AddTenantRedirectUriAsync(
                            _keycloakOptions.PrimaryRealmName,
                            request.TenantId,
                            cancellationToken);
                    }
                }
            }

            // Phase 3: Register tenant in system DB
            await RegisterTenantAsync(request, databaseName, realmName, clientSecret, companyId, cancellationToken);

            LogProvisioningCompleted(_logger, request.TenantId, databaseName);
            return TenantProvisionResult.Created(databaseName, realmName, authority);
        }
        catch (Exception ex)
        {
            LogProvisioningFailed(_logger, request.TenantId, ex);

            // Rollback must not be bound to the caller's token — use CancellationToken.None
            // so that cancellation does not orphan partially-created resources.
            if (realmCreated)
            {
                await _keycloakProvisioner.DeleteRealmAsync(realmName, CancellationToken.None);
            }

            if (databaseCreated)
            {
                await RollbackAsync(request.TenantId, databaseName);
            }

            return TenantProvisionResult.Failed("Provisioning failed. See server logs for details.");
        }
    }

    public async Task<ReprovisionResult> ReprovisionAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        LogReprovisionStarted(_logger, tenantId);

        try
        {
            // Look up the tenant — distinguish "not found" from "deactivated".
            var tenant = await GetTenantForReprovisionAsync(tenantId, cancellationToken);
            if (tenant is null)
            {
                return ReprovisionResult.NotFound(tenantId);
            }

            if (!tenant.IsActive)
            {
                return ReprovisionResult.Deactivated(tenantId);
            }

            // Run pending migrations on the tenant database.
            var databaseName = tenant.DatabaseName;
            var migrationsApplied = await RunPendingMigrationsAsync(databaseName, cancellationToken);

            LogReprovisionCompleted(_logger, tenantId, databaseName, migrationsApplied);
            return ReprovisionResult.Completed(databaseName, migrationsApplied);
        }
        catch (Exception ex)
        {
            LogReprovisionFailed(_logger, tenantId, ex);
            return ReprovisionResult.Failed("Reprovisioning failed. See server logs for details.");
        }
    }

    public async Task<DeactivationResult> DeactivateAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        LogDeactivationStarted(_logger, tenantId);

        try
        {
            // Look up the tenant to get realm_name and check status.
            var tenant = await GetTenantForDeactivationAsync(tenantId, cancellationToken);
            if (tenant is null)
            {
                return DeactivationResult.NotFound(tenantId);
            }

            if (!tenant.IsActive)
            {
                LogTenantAlreadyDeactivated(_logger, tenantId);
                return DeactivationResult.AlreadyInactive(tenantId);
            }

            // Phase 1: Soft-delete in system DB (reversible -- do this first).
            try
            {
                await SoftDeleteTenantAsync(tenantId, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Concurrent deactivation between lookup and update -- treat as idempotent.
                LogTenantAlreadyDeactivated(_logger, tenantId);
                return DeactivationResult.AlreadyInactive(tenantId);
            }

            // Phase 2: Delete Keycloak realm (irreversible, best-effort).
            if (_keycloakOptions.IsConfigured && !string.IsNullOrEmpty(tenant.RealmName))
            {
                await _keycloakProvisioner.DeleteRealmAsync(tenant.RealmName, cancellationToken);

                // Unregister from in-memory realm registry so JWT validation rejects
                // tokens from the deleted realm immediately.
                var baseUrl = _keycloakOptions.AdminBaseUrl.TrimEnd('/');
                var authority = $"{baseUrl}/realms/{tenant.RealmName}";
                _realmRegistry.UnregisterRealm(tenant.RealmName, authority);
            }

            LogDeactivationCompleted(_logger, tenantId);
            return DeactivationResult.Completed();
        }
        catch (Exception ex)
        {
            LogDeactivationFailed(_logger, tenantId, ex);
            return DeactivationResult.Failed("Deactivation failed. See server logs for details.");
        }
    }

    private static string GenerateClientSecret()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9\-]{0,61}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex TenantIdRegex();

    [LoggerMessage(Level = LogLevel.Information, Message = "No active tenants found — skipping incremental migration")]
    private static partial void LogNoTenantsToMigrate(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting incremental migration for {Count} active tenant(s)")]
    private static partial void LogTenantMigrationStarted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tenant '{TenantId}' (database '{DatabaseName}') migrations applied")]
    private static partial void LogTenantMigrated(ILogger logger, string tenantId, string databaseName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping migration for tenant '{TenantId}' (database '{DatabaseName}') — inaccessible")]
    private static partial void LogTenantMigrationSkipped(ILogger logger, string tenantId, string databaseName, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Migration failed for tenant '{TenantId}' (database '{DatabaseName}')")]
    private static partial void LogTenantMigrationFailed(ILogger logger, string tenantId, string databaseName, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Incremental tenant migration completed: {Migrated}/{Total} tenant(s) updated")]
    private static partial void LogTenantMigrationCompleted(ILogger logger, int migrated, int total);

    [LoggerMessage(Level = LogLevel.Information, Message = "Provisioning tenant '{TenantId}' with database '{DatabaseName}'")]
    private static partial void LogProvisioningStarted(ILogger logger, string tenantId, string databaseName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tenant '{TenantId}' already provisioned - returning idempotent success")]
    private static partial void LogTenantAlreadyProvisioned(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Database '{DatabaseName}' created")]
    private static partial void LogDatabaseCreated(ILogger logger, string databaseName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Migrations applied for tenant database '{DatabaseName}'")]
    private static partial void LogMigrationsApplied(ILogger logger, string databaseName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tenant '{TenantId}' registered in tenants table")]
    private static partial void LogTenantRegistered(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Provisioning completed for tenant '{TenantId}' (database '{DatabaseName}')")]
    private static partial void LogProvisioningCompleted(ILogger logger, string tenantId, string databaseName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Provisioning failed for tenant '{TenantId}'")]
    private static partial void LogProvisioningFailed(ILogger logger, string tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reprovisioning tenant '{TenantId}' — re-running pending migrations")]
    private static partial void LogReprovisionStarted(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reprovisioning completed for tenant '{TenantId}' (database '{DatabaseName}', {MigrationsApplied} migrations applied)")]
    private static partial void LogReprovisionCompleted(ILogger logger, string tenantId, string databaseName, int migrationsApplied);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reprovisioning failed for tenant '{TenantId}'")]
    private static partial void LogReprovisionFailed(ILogger logger, string tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deactivating tenant '{TenantId}'")]
    private static partial void LogDeactivationStarted(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tenant '{TenantId}' already deactivated - returning idempotent success")]
    private static partial void LogTenantAlreadyDeactivated(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tenant '{TenantId}' deactivated (realm deleted, registry cleared, soft-deleted in DB)")]
    private static partial void LogDeactivationCompleted(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Deactivation failed for tenant '{TenantId}'")]
    private static partial void LogDeactivationFailed(ILogger logger, string tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rolling back provisioning for tenant '{TenantId}' (dropping database '{DatabaseName}')")]
    private static partial void LogRollbackStarted(ILogger logger, string tenantId, string databaseName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rollback completed for tenant '{TenantId}' (database '{DatabaseName}' dropped)")]
    private static partial void LogRollbackCompleted(ILogger logger, string tenantId, string databaseName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rollback failed for tenant '{TenantId}' - manual cleanup may be required")]
    private static partial void LogRollbackFailed(ILogger logger, string tenantId, Exception exception);

    private static bool IsSystemOnlyMigration(string scriptName)
    {
        // Extract filename from the embedded resource name (last segment after '.')
        // Embedded resource names look like: Namespace.Migrations.V005__create_grid_preferences.sql
        foreach (var prefix in SystemOnlyMigrationPrefixes)
        {
            if (scriptName.Contains(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string QuoteIdentifier(string identifier)
    {
        // Double any embedded quotes per SQL standard, then wrap in quotes.
        return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static async Task EnsureJournalSchemaAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE SCHEMA IF NOT EXISTS outbox;";
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<DeactivationTenantRow?> GetTenantForDeactivationAsync(string tenantId, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_defaultConnectionString);
        await connection.OpenAsync(ct);

        return await connection.QuerySingleOrDefaultAsync<DeactivationTenantRow>(
            new CommandDefinition(
                "SELECT realm_name AS realmname, is_active AS isactive FROM outbox.tenants WHERE id = @TenantId",
                new { TenantId = tenantId },
                cancellationToken: ct));
    }

    /// <summary>
    /// Sets <c>is_active = FALSE</c> on the tenant row.
    /// Throws if the row was concurrently deleted between lookup and update.
    /// </summary>
    private async Task SoftDeleteTenantAsync(string tenantId, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_defaultConnectionString);
        await connection.OpenAsync(ct);

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                "UPDATE outbox.tenants SET is_active = FALSE WHERE id = @TenantId AND is_active = TRUE",
                new { TenantId = tenantId },
                cancellationToken: ct));

        if (affected == 0)
        {
            throw new InvalidOperationException(
                $"Tenant '{tenantId}' was not updated (concurrent deletion or already inactive).");
        }
    }

    private async Task<TenantRow?> GetTenantForReprovisionAsync(string tenantId, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_defaultConnectionString);
        await connection.OpenAsync(ct);

        return await connection.QuerySingleOrDefaultAsync<TenantRow>(
            new CommandDefinition(
                "SELECT database_name AS databasename, is_active AS isactive FROM outbox.tenants WHERE id = @TenantId",
                new { TenantId = tenantId },
                cancellationToken: ct));
    }

    /// <summary>
    /// Runs pending migrations on a tenant database and returns the count of scripts applied.
    /// DbUp's journal table tracks which migrations have already run, so only new ones execute.
    /// </summary>
    private async Task<int> RunPendingMigrationsAsync(string databaseName, CancellationToken ct)
    {
        var upgrader = await BuildTenantUpgraderAsync(databaseName, ct);
        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"Migrations failed for database '{databaseName}': {result.Error.Message}",
                result.Error);
        }

        return result.Scripts.Count();
    }

    private async Task<bool> TenantExistsAsync(string tenantId, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_defaultConnectionString);
        await connection.OpenAsync(ct);

        var exists = await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                "SELECT EXISTS(SELECT 1 FROM outbox.tenants WHERE id = @TenantId)",
                new { TenantId = tenantId },
                cancellationToken: ct));

        return exists;
    }

    private async Task CreateDatabaseAsync(string databaseName, CancellationToken ct)
    {
        // CREATE DATABASE cannot run inside a transaction — use a raw connection.
        await using var connection = new NpgsqlConnection(_defaultConnectionString);
        await connection.OpenAsync(ct);

        var quotedName = QuoteIdentifier(databaseName);
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"CREATE DATABASE {quotedName}",
                cancellationToken: ct));

        LogDatabaseCreated(_logger, databaseName);
    }

    /// <summary>
    /// Runs module migrations AND tenant-relevant infrastructure migrations
    /// (audit, grid preferences, saved filters) in the newly created tenant database.
    /// System-only migrations (outbox, tenants registry) are excluded.
    /// </summary>
    private async Task RunTenantMigrationsAsync(string databaseName, CancellationToken ct)
    {
        var upgrader = await BuildTenantUpgraderAsync(databaseName, ct);
        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"Migrations failed for database '{databaseName}': {result.Error.Message}",
                result.Error);
        }

        LogMigrationsApplied(_logger, databaseName);
    }

    /// <summary>
    /// Builds a DbUp <see cref="DbUp.Engine.UpgradeEngine"/> configured for a tenant database.
    /// Includes all module migrations and tenant-relevant infrastructure migrations,
    /// excluding system-only scripts (tenant registry tables).
    /// </summary>
    private async Task<DbUp.Engine.UpgradeEngine> BuildTenantUpgraderAsync(string databaseName, CancellationToken ct)
    {
        var tenantConnectionString = new NpgsqlConnectionStringBuilder(_defaultConnectionString)
        {
            Database = databaseName,
        }.ToString();

        await EnsureJournalSchemaAsync(tenantConnectionString, ct);

        var builder = DeployChanges.To
            .PostgresqlDatabase(tenantConnectionString);

        foreach (var assembly in _migrationOptions.Assemblies)
        {
            builder = builder.WithScriptsEmbeddedInAssembly(
                assembly,
                s => s.Contains(".Migrations.", StringComparison.Ordinal));
        }

        var infraAssembly = typeof(TenantProvisioningService).Assembly;
        builder = builder.WithScriptsEmbeddedInAssembly(
            infraAssembly,
            s => s.Contains(".Migrations.", StringComparison.Ordinal)
                 && !IsSystemOnlyMigration(s));

        return builder
            .JournalToPostgresqlTable("outbox", "schema_versions")
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build();
    }

    private async Task RegisterTenantAsync(
        TenantProvisionRequest request,
        string databaseName,
        string realmName,
        string clientSecret,
        Guid companyId,
        CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_defaultConnectionString);
        await connection.OpenAsync(ct);

        const string sql = """
            INSERT INTO outbox.tenants (id, display_name, admin_email, database_name, realm_name, client_secret, company_id)
            VALUES (@TenantId, @DisplayName, @AdminEmail, @DatabaseName, @RealmName, @ClientSecret, @CompanyId)
            ON CONFLICT (id) DO NOTHING
            """;

        await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    TenantId = request.TenantId,
                    request.DisplayName,
                    request.AdminEmail,
                    DatabaseName = databaseName,
                    RealmName = realmName,
                    ClientSecret = clientSecret,
                    CompanyId = companyId,
                },
                cancellationToken: ct));

        LogTenantRegistered(_logger, request.TenantId);
    }

    private async Task RollbackAsync(string tenantId, string databaseName)
    {
        try
        {
            LogRollbackStarted(_logger, tenantId, databaseName);

            await using var connection = new NpgsqlConnection(_defaultConnectionString);
            await connection.OpenAsync(CancellationToken.None);

            // Remove registry entry (if it was inserted before the failure).
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM outbox.tenants WHERE id = @TenantId",
                    new { TenantId = tenantId },
                    cancellationToken: CancellationToken.None));

            // Drop the partially-created database.
            // WITH (FORCE) terminates active connections (PostgreSQL 13+).
            var quotedName = QuoteIdentifier(databaseName);
            await connection.ExecuteAsync(
                new CommandDefinition(
                    $"DROP DATABASE IF EXISTS {quotedName} WITH (FORCE)",
                    cancellationToken: CancellationToken.None));

            LogRollbackCompleted(_logger, tenantId, databaseName);
        }
        catch (Exception ex)
        {
            // Rollback is best-effort. Log and move on.
            LogRollbackFailed(_logger, tenantId, ex);
        }
    }

    /// <summary>Lightweight Dapper target for listing tenant IDs and database names.</summary>
    private sealed class TenantRecord
    {
        public string Id { get; init; } = default!;

        public string DatabaseName { get; init; } = default!;
    }

    private sealed class TenantRow
    {
        public string DatabaseName { get; init; } = default!;

        public bool IsActive { get; init; }
    }

    private sealed class DeactivationTenantRow
    {
        public string? RealmName { get; init; }

        public bool IsActive { get; init; }
    }
}
