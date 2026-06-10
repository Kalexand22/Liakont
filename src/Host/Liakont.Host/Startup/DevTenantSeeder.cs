namespace Liakont.Host.Startup;

using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Seed de DÉVELOPPEMENT du tenant par défaut (bug-inbox « amorçage console ») : enregistre dans
/// <c>outbox.tenants</c> le tenant configuré, rattaché au realm Keycloak de dev déjà importé
/// (deploy/docker/keycloak/realm-export.json), pour que la console soit testable depuis les artefacts
/// committés sans intervention manuelle. Sans cette ligne, toute mutation tenant-scopée échoue
/// (« Aucun tenant résolu ») et le seul chemin de provisioning (/admin/tenants) exige un SystemAdmin
/// lui-même non amorcé.
/// <para>
/// Garde-fous : ne tourne QUE si l'environnement est Development ET que la section
/// <c>DevTenantSeed</c> est configurée (appsettings.Development.json) — jamais en production, où le
/// provisioning passe par /admin/tenants (TenantProvisioningService, realm dédié par tenant).
/// Idempotent (ON CONFLICT DO NOTHING). N'écrit que dans la base SYSTÈME Liakont, jamais dans une
/// base source client (CLAUDE.md n°5).
/// </para>
/// </summary>
internal static partial class DevTenantSeeder
{
    /// <summary>Insère le tenant de dev s'il n'existe pas. Appelé AVANT la migration des tenants existants.</summary>
    public static async Task SeedDevTenantAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        var options = app.Configuration.GetSection("DevTenantSeed").Get<DevTenantSeedOptions>();
        if (options is null || string.IsNullOrWhiteSpace(options.TenantId))
        {
            return;
        }

        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Liakont.Host.Startup.DevTenantSeeder");

        // realm_name et database_name sont NOT NULL dans outbox.tenants : une configuration partielle
        // est une erreur d'amorçage à signaler, pas à insérer à moitié.
        if (string.IsNullOrWhiteSpace(options.RealmName) || string.IsNullOrWhiteSpace(options.DatabaseName))
        {
            LogDevTenantSeedIncomplete(logger, options.TenantId);
            return;
        }

        var connectionString = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // ON CONFLICT sans cible : ignore AUSSI un conflit d'unicité sur realm_name/database_name
        // (tenant déjà rattaché autrement) — le seed de dev ne doit jamais empêcher le démarrage.
        const string sql = """
            INSERT INTO outbox.tenants (id, display_name, admin_email, database_name, realm_name, client_secret)
            VALUES (@id, @displayName, @adminEmail, @databaseName, @realmName, @clientSecret)
            ON CONFLICT DO NOTHING
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", options.TenantId);
        command.Parameters.AddWithValue("displayName", options.DisplayName);
        command.Parameters.AddWithValue("adminEmail", options.AdminEmail);
        command.Parameters.AddWithValue("databaseName", options.DatabaseName);
        command.Parameters.AddWithValue("realmName", options.RealmName);
        command.Parameters.AddWithValue("clientSecret", (object?)options.ClientSecret ?? DBNull.Value);

        var inserted = await command.ExecuteNonQueryAsync();
        if (inserted > 0)
        {
            LogDevTenantSeeded(logger, options.TenantId, options.RealmName);
        }
        else
        {
            LogDevTenantAlreadyPresent(logger, options.TenantId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seed du tenant de dev « {TenantId} » ignoré : RealmName et DatabaseName sont requis (section DevTenantSeed).")]
    private static partial void LogDevTenantSeedIncomplete(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tenant de dev « {TenantId} » amorcé dans outbox.tenants (realm « {RealmName} »).")]
    private static partial void LogDevTenantSeeded(ILogger logger, string tenantId, string realmName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tenant de dev « {TenantId} » déjà présent — seed ignoré.")]
    private static partial void LogDevTenantAlreadyPresent(ILogger logger, string tenantId);
}
