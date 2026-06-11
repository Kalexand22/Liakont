namespace Liakont.Host.Startup;

using System.Globalization;
using Liakont.Host.PaAccounts;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.Transmission.Contracts;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Abstractions.MultiTenancy;
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

    /// <summary>
    /// Importe (idempotent) le profil de paramétrage FICTIF du tenant de dev dans SA base, APRÈS sa
    /// migration (le schéma <c>tenantsettings</c> n'existe qu'à ce stade). Sans profil, le CHECK suspend
    /// tout document (CFG02, bug-inbox) : cet amorçage donne à un environnement de dev vierge un profil
    /// valide pour que le parcours ingéré→émis se déroule sans intervention SQL. Tenant-scopé via
    /// <see cref="ITenantScopeFactory"/> (même seam que le CHECK / TenantJobRunner) ; le companyId est
    /// explicite (claim <c>company_id</c> du realm de dev). N'écrit AUCUN secret (INV-TENANTSETTINGS-007).
    /// <para>
    /// Garde-fous : Development uniquement ; nécessite <c>DevTenantSeed</c> avec un <c>CompanyId</c> et un
    /// <c>SeedDirectoryPath</c>. Strictement NON FATAL : toute défaillance (dossier absent, import en
    /// erreur) est journalisée et n'empêche jamais le démarrage.
    /// </para>
    /// </summary>
    public static async Task SeedDevTenantProfileAsync(this WebApplication app)
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

        // Sans companyId ou sans dossier de seed : seul le tenant système est amorcé (profil non importé).
        if (options.CompanyId == Guid.Empty || string.IsNullOrWhiteSpace(options.SeedDirectoryPath))
        {
            LogDevProfileSeedSkipped(logger, options.TenantId);
            return;
        }

        // Chemin relatif résolu par rapport au ContentRoot (en dev : src/Host/Liakont.Host).
        var seedDir = Path.IsPathRooted(options.SeedDirectoryPath)
            ? options.SeedDirectoryPath
            : Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, options.SeedDirectoryPath));

        if (!Directory.Exists(seedDir))
        {
            LogDevProfileSeedDirectoryMissing(logger, seedDir, options.TenantId);
            return;
        }

        try
        {
            await using var scope = app.Services.GetRequiredService<ITenantScopeFactory>().Create(options.TenantId);

            // Idempotent / non destructif : si le profil existe déjà, ne PAS réimporter (un ré-import
            // remettrait à la baseline du seed des réglages éventuellement modifiés via la console).
            var settingsQueries = scope.Services.GetRequiredService<ITenantSettingsQueries>();
            if (await settingsQueries.GetCurrentCompanyId() is not null)
            {
                LogDevProfileAlreadyConfigured(logger, options.TenantId);
                return;
            }

            var sender = scope.Services.GetRequiredService<ISender>();
            var result = await sender.Send(new ImportTenantSeedCommand
            {
                SeedDirectoryPath = seedDir,
                CompanyId = options.CompanyId,
            });

            LogDevProfileSeeded(logger, options.TenantId, result.ProfileImported, result.FiscalImported, result.PaAccountsImported);
        }
        catch (Exception ex)
        {
            // NON FATAL : l'amorçage du profil de dev ne doit jamais empêcher le démarrage.
            LogDevProfileSeedFailed(logger, options.TenantId, ex);
        }
    }

    /// <summary>
    /// Publie le SIREN / active le <c>tax_report_setting</c> du compte PA actif (Fake) du tenant de dev, à
    /// CHAQUE démarrage (décision E1, point 2 ; FIX201). C'est l'étape d'onboarding qui rend l'envoi
    /// EXERÇABLE : sans elle, le diagnostic pré-envoi (F04 §3.1) répond « Transport not available » et aucun
    /// document n'est jamais émis. Appelée APRÈS l'amorçage du profil (le compte PA et le SIREN doivent
    /// exister). Idempotente (<c>EnsureTaxReportSettingAsync</c>) et REJOUÉE à chaque démarrage : l'état du
    /// plug-in factice vit en mémoire (par compte) et est perdu au redémarrage du processus.
    /// <para>
    /// Garde-fous : Development uniquement ; nécessite <c>DevTenantSeed:TaxReportSetting</c> (valeurs FICTIVES
    /// pour le Fake — jamais une vraie PA). Strictement NON FATAL : toute défaillance est journalisée et
    /// n'empêche jamais le démarrage. N'écrit qu'auprès du plug-in factice en mémoire (aucune base source).
    /// </para>
    /// </summary>
    public static async Task SeedDevTenantPublicationAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        var options = app.Configuration.GetSection("DevTenantSeed").Get<DevTenantSeedOptions>();
        if (options is null || string.IsNullOrWhiteSpace(options.TenantId) || options.CompanyId == Guid.Empty)
        {
            return;
        }

        var publication = options.TaxReportSetting;
        if (publication is null
            || string.IsNullOrWhiteSpace(publication.StartDate)
            || string.IsNullOrWhiteSpace(publication.TypeOperation)
            || string.IsNullOrWhiteSpace(publication.EnterpriseSize))
        {
            // Publication de dev non configurée : l'opérateur publiera via la console (action FIX201).
            return;
        }

        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Liakont.Host.Startup.DevTenantSeeder");

        if (!DateOnly.TryParse(publication.StartDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate))
        {
            LogDevPublicationBadDate(logger, publication.StartDate, options.TenantId);
            return;
        }

        try
        {
            await using var scope = app.Services.GetRequiredService<ITenantScopeFactory>().Create(options.TenantId);

            var settingsQueries = scope.Services.GetRequiredService<ITenantSettingsQueries>();
            var companyId = await settingsQueries.GetCurrentCompanyId();
            if (companyId is null)
            {
                LogDevPublicationNoProfile(logger, options.TenantId);
                return;
            }

            var accounts = await settingsQueries.GetPaAccounts(companyId.Value);
            var active = accounts.FirstOrDefault(a => a.IsActive);
            if (active is null)
            {
                LogDevPublicationNoAccount(logger, options.TenantId);
                return;
            }

            var registry = scope.Services.GetRequiredService<IPaClientRegistry>();
            if (!registry.IsRegistered(active.PluginType))
            {
                LogDevPublicationNoPlugin(logger, active.PluginType, options.TenantId);
                return;
            }

            // MÊME descripteur que SendTenantJob (PaType + TenantId) → MÊME instance factice (cache par compte),
            // donc le réglage publié ici est vu par l'envoi qui suit (FIX201 lifetime).
            var request = TaxReportSettingRequestBuilder.Build(
                startDate, publication.TypeOperation.Trim(), publication.EnterpriseSize.Trim(), publication.NafCode);
            var client = registry.Resolve(new PaAccountDescriptor(active.PluginType, options.TenantId));
            await client.EnsureTaxReportSettingAsync(request);

            LogDevPublicationDone(logger, active.PluginType, options.TenantId, startDate);
        }
        catch (Exception ex)
        {
            // NON FATAL : l'amorçage de la publication de dev ne doit jamais empêcher le démarrage.
            LogDevPublicationFailed(logger, options.TenantId, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seed du tenant de dev « {TenantId} » ignoré : RealmName et DatabaseName sont requis (section DevTenantSeed).")]
    private static partial void LogDevTenantSeedIncomplete(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publication de dev ignorée : date de début « {StartDate} » invalide (format attendu yyyy-MM-dd) pour « {TenantId} ».")]
    private static partial void LogDevPublicationBadDate(ILogger logger, string startDate, string tenantId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Publication de dev ignorée pour « {TenantId} » : aucun profil tenant (companyId) — amorcez d'abord le profil.")]
    private static partial void LogDevPublicationNoProfile(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publication de dev ignorée pour « {TenantId} » : aucun compte Plateforme Agréée actif.")]
    private static partial void LogDevPublicationNoAccount(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publication de dev ignorée pour « {TenantId} » : le plug-in « {PluginType} » n'est pas câblé (PaClients).")]
    private static partial void LogDevPublicationNoPlugin(ILogger logger, string pluginType, string tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Publication de dev effectuée pour « {TenantId} » : SIREN publié auprès de « {PluginType} » (début {StartDate:yyyy-MM-dd}) — l'envoi est exerçable.")]
    private static partial void LogDevPublicationDone(ILogger logger, string pluginType, string tenantId, DateOnly startDate);

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec de la publication de dev pour « {TenantId} » (non fatal — le démarrage continue).")]
    private static partial void LogDevPublicationFailed(ILogger logger, string tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Amorçage du profil de dev ignoré pour « {TenantId} » : CompanyId et SeedDirectoryPath sont requis (section DevTenantSeed).")]
    private static partial void LogDevProfileSeedSkipped(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Amorçage du profil de dev ignoré : dossier de seed introuvable « {SeedDir} » (tenant « {TenantId} »).")]
    private static partial void LogDevProfileSeedDirectoryMissing(ILogger logger, string seedDir, string tenantId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Amorçage du profil de dev ignoré pour « {TenantId} » : un profil existe déjà (ré-import non destructif évité).")]
    private static partial void LogDevProfileAlreadyConfigured(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Profil de dev amorcé pour « {TenantId} » (profil={ProfileImported}, fiscal={FiscalImported}, comptes PA={PaAccounts}).")]
    private static partial void LogDevProfileSeeded(ILogger logger, string tenantId, bool profileImported, bool fiscalImported, int paAccounts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec de l'amorçage du profil de dev pour « {TenantId} » (non fatal — le démarrage continue).")]
    private static partial void LogDevProfileSeedFailed(ILogger logger, string tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tenant de dev « {TenantId} » amorcé dans outbox.tenants (realm « {RealmName} »).")]
    private static partial void LogDevTenantSeeded(ILogger logger, string tenantId, string realmName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tenant de dev « {TenantId} » déjà présent — seed ignoré.")]
    private static partial void LogDevTenantAlreadyPresent(ILogger logger, string tenantId);
}
