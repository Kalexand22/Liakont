namespace Liakont.Host.Startup;

using System.Globalization;
using System.Text.RegularExpressions;
using Liakont.Host.MultiTenancy;
using Liakont.Host.PaAccounts;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.TvaMapping.Contracts.Commands;
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
    /// <summary>Action d'amorçage à exécuter, décidée par <see cref="DecideSeedAction"/>.</summary>
    internal enum DevSeedAction
    {
        /// <summary>Boot à froid (aucun paramétrage) : importer le seed complet (paramétrage + table) puis poser l'identité de dev.</summary>
        ImportFullSeed,

        /// <summary>Paramétrage présent + un fichier de mapping est disponible : rattraper la SEULE table (idempotent).</summary>
        BackfillMappingTable,

        /// <summary>Paramétrage présent, aucun fichier de mapping à amorcer : rien à faire.</summary>
        Skip,
    }

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

        // realm_name et database_name sont NOT NULL dans outbox.tenants ; company_id l'est devenu en
        // RLM02 (V017) car il pilote la résolution autoritaire du tenant (ADR-0021 §2c) — une
        // configuration partielle est une erreur d'amorçage à signaler, pas à insérer à moitié.
        if (string.IsNullOrWhiteSpace(options.RealmName)
            || string.IsNullOrWhiteSpace(options.DatabaseName)
            || options.CompanyId == Guid.Empty)
        {
            LogDevTenantSeedIncomplete(logger, options.TenantId);
            return;
        }

        var connectionString = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // ON CONFLICT sans cible : ignore AUSSI un conflit d'unicité sur realm_name/database_name
        // (tenant déjà rattaché autrement) — le seed de dev ne doit jamais empêcher le démarrage.
        // company_id explicite (RLM02) : DOIT coïncider avec le claim company_id du realm de dev
        // (deploy/docker/keycloak/realm-export.json) et le backfill V017 — cohérence des 3 sources
        // gardée par DefaultCompanyIdCoherenceTests. Sans lui, un boot à froid violerait le NOT NULL.
        const string sql = """
            INSERT INTO outbox.tenants (id, display_name, admin_email, database_name, realm_name, client_secret, company_id)
            VALUES (@id, @displayName, @adminEmail, @databaseName, @realmName, @clientSecret, @companyId)
            ON CONFLICT DO NOTHING
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", options.TenantId);
        command.Parameters.AddWithValue("displayName", options.DisplayName);
        command.Parameters.AddWithValue("adminEmail", options.AdminEmail);
        command.Parameters.AddWithValue("databaseName", options.DatabaseName);
        command.Parameters.AddWithValue("realmName", options.RealmName);
        command.Parameters.AddWithValue("clientSecret", (object?)options.ClientSecret ?? DBNull.Value);
        command.Parameters.AddWithValue("companyId", options.CompanyId);

        var inserted = await command.ExecuteNonQueryAsync();
        if (inserted > 0)
        {
            LogDevTenantSeeded(logger, options.TenantId, options.RealmName);
        }
        else
        {
            LogDevTenantAlreadyPresent(logger, options.TenantId);
        }

        // RLM01 : tenants de recette additionnels, amorcés AVEC leur company_id DISTINCT (le default garde
        // company_id NULL — backfillé en RLM02). Rend l'isolation par claim prouvable de bout en bout
        // (deux utilisateurs réels → deux company_id). Réutilise la même connexion ; idempotent.
        await SeedAdditionalDevTenantsAsync(connection, options.AdditionalTenants, logger);
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
            var sender = scope.Services.GetRequiredService<ISender>();

            // Amorçage idempotent PAR COMPOSANT (FIX203a) — décision extraite dans DecideSeedAction.
            // BUG-14 : l'identité n'est plus seedée, le profil ne marque donc plus « tenant paramétré » ;
            // on ancre l'idempotence sur la présence d'UN composant de paramétrage (fiscal/planif/seuils/
            // compte PA — un seed peut n'écrire que l'un d'eux), scopée sur le companyId de DEV fixé en config.
            var settingsQueries = scope.Services.GetRequiredService<ITenantSettingsQueries>();
            var alreadyConfigured = await settingsQueries.HasAnyConfigurationAsync(options.CompanyId);
            var mappingFilePath = Path.Combine(seedDir, ImportTenantSeedCommand.MappingSeedFileName);

            switch (DecideSeedAction(alreadyConfigured, File.Exists(mappingFilePath)))
            {
                case DevSeedAction.ImportFullSeed:
                {
                    var result = await sender.Send(new ImportTenantSeedCommand
                    {
                        SeedDirectoryPath = seedDir,
                        CompanyId = options.CompanyId,
                    });

                    LogDevProfileSeeded(logger, options.TenantId, result.FiscalImported, result.PaAccountsImported);
                    break;
                }

                case DevSeedAction.BackfillMappingTable:
                {
                    // Réglages déjà présents : ne JAMAIS rejouer le paramétrage (un ré-import écraserait des
                    // réglages édités via la console) ; rattraper la SEULE table qui a pu manquer, scopée sur
                    // le companyId de DEV fixé en configuration (BUG-14 : plus de profil seedé d'où déduire la
                    // société — le tenant de dev a un companyId stable). Import idempotent.
                    var mappingImported = await sender.Send(new ImportMappingTableSeedCommand
                    {
                        SeedFilePath = mappingFilePath,
                        CompanyId = options.CompanyId,
                    });

                    if (mappingImported)
                    {
                        LogDevMappingBackfilled(logger, options.TenantId);
                    }
                    else
                    {
                        LogDevProfileAlreadyConfigured(logger, options.TenantId);
                    }

                    break;
                }

                default:
                    LogDevProfileAlreadyConfigured(logger, options.TenantId);
                    break;
            }

            // L'identité légale n'est jamais seedée (BUG-14) : on la pose PROGRAMMATIQUEMENT (depuis la config
            // de dev, HORS fichier de seed) dès qu'aucun profil n'existe — au boot à froid après l'import du
            // paramétrage, ET en auto-réparation d'un split-brain « paramétrage présent / profil absent » (un
            // boot précédent a pu committer le paramétrage puis échouer à poser le profil). Sans profil, le
            // pipeline de dev reste suspendu (CFG02). Idempotent (upsert) ; sans effet si le profil existe déjà.
            // Ordre seed→profil : la garde override n'honore le companyId explicite que tant qu'aucun profil
            // n'existe (le seed n'en crée pas).
            if (await settingsQueries.GetCurrentCompanyId() is null)
            {
                await CreateDevProfileAsync(sender, options, logger);
            }
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
    /// document n'est jamais émis. Appelée APRÈS l'amorçage du paramétrage (le compte PA actif doit exister ;
    /// le SIREN n'est plus requis ici — il n'est pas seedé depuis BUG-14 et <c>TaxReportSettingRequestBuilder.Build</c>
    /// ne l'utilise pas). Idempotente (<c>EnsureTaxReportSettingAsync</c>) et REJOUÉE à chaque démarrage : l'état
    /// du plug-in factice vit en mémoire (par compte) et est perdu au redémarrage du processus.
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

            // BUG-14 : l'identité n'est plus seedée (aucun profil au boot) ; on s'appuie sur le companyId de
            // DEV fixé en configuration (validé non vide ci-dessus) pour résoudre le compte PA à publier.
            var settingsQueries = scope.Services.GetRequiredService<ITenantSettingsQueries>();
            var accounts = await settingsQueries.GetPaAccounts(options.CompanyId);
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

    /// <summary>
    /// Décision d'amorçage idempotente PAR COMPOSANT (FIX203a), pure et testable hors d'un WebApplication.
    /// Le paramétrage TenantSettings (fiscal, comptes PA, planification, seuils) est importé en UNE
    /// transaction — il n'est donc jamais partiel ; seule la table de mapping TVA (transaction distincte)
    /// peut rester absente après un échec, et c'est le seul composant à rattraper sans rejouer (ni écraser)
    /// le reste du paramétrage.
    /// </summary>
    /// <param name="alreadyConfigured">Le tenant a déjà du paramétrage (un import a déjà eu lieu).</param>
    /// <param name="mappingSeedFileExists">Un fichier de seed de mapping TVA est disponible dans le dossier.</param>
    internal static DevSeedAction DecideSeedAction(bool alreadyConfigured, bool mappingSeedFileExists)
    {
        if (!alreadyConfigured)
        {
            return DevSeedAction.ImportFullSeed;
        }

        return mappingSeedFileExists ? DevSeedAction.BackfillMappingTable : DevSeedAction.Skip;
    }

    /// <summary>
    /// Identifiant de base PostgreSQL sûr : minuscules, chiffres et underscores, commençant par une
    /// lettre, au plus 63 caractères (limite d'identifiant PostgreSQL). Couvre le nom dérivé
    /// <c>stratum_&lt;id&gt;</c> et ferme l'injection dans le <c>CREATE DATABASE</c> (non paramétrable).
    /// </summary>
    internal static bool IsSafeDatabaseIdentifier(string databaseName) =>
        !string.IsNullOrEmpty(databaseName)
        && databaseName.Length <= 63
        && DatabaseIdentifierRegex().IsMatch(databaseName);

    /// <summary>
    /// Pose PROGRAMMATIQUEMENT l'identité légale FICTIVE du tenant de dev (depuis <c>DevTenantSeed:Profile</c>),
    /// au boot à froid uniquement (cas <see cref="DevSeedAction.ImportFullSeed"/>). L'identité n'est JAMAIS
    /// seedée par fichier (BUG-14) : ce chemin de dev la fournit HORS du seed, comme le ferait le provisioning
    /// de démo. La commande est un upsert (idempotente) et l'appel est encapsulé dans le try NON FATAL de
    /// l'appelant. Sans section <c>Profile</c> (ou SIREN vide), l'amorçage est ignoré : le pipeline restera
    /// suspendu jusqu'à une saisie via la console (CFG02).
    /// </summary>
    private static async Task CreateDevProfileAsync(ISender sender, DevTenantSeedOptions options, ILogger logger)
    {
        var profile = options.Profile;
        if (profile is null || string.IsNullOrWhiteSpace(profile.Siren))
        {
            LogDevProfileIdentitySkipped(logger, options.TenantId);
            return;
        }

        await sender.Send(new SaveTenantProfileCommand
        {
            CompanyId = options.CompanyId,
            Siren = profile.Siren.Trim(),
            RaisonSociale = profile.RaisonSociale,
            Street = profile.Street,
            PostalCode = profile.PostalCode,
            City = profile.City,
            Country = profile.Country,
            ContactEmailAlerte = string.IsNullOrWhiteSpace(profile.ContactEmailAlerte)
                ? null
                : profile.ContactEmailAlerte.Trim(),
        });

        LogDevProfileIdentitySeeded(logger, options.TenantId);
    }

    /// <summary>
    /// Amorce les tenants de recette additionnels dans <c>outbox.tenants</c> AVEC leur <c>company_id</c>.
    /// Une entrée incomplète (champ NOT NULL/UNIQUE manquant ou company_id vide) est signalée et ignorée —
    /// jamais d'insert à moitié. Idempotent (<c>ON CONFLICT DO NOTHING</c>).
    /// </summary>
    private static async Task SeedAdditionalDevTenantsAsync(
        NpgsqlConnection connection,
        IReadOnlyList<DevAdditionalTenantOptions> additionalTenants,
        ILogger logger)
    {
        const string sql = """
            INSERT INTO outbox.tenants (id, display_name, admin_email, database_name, realm_name, client_secret, company_id)
            VALUES (@id, @displayName, @adminEmail, @databaseName, @realmName, @clientSecret, @companyId)
            ON CONFLICT DO NOTHING
            """;

        foreach (var tenant in additionalTenants)
        {
            if (string.IsNullOrWhiteSpace(tenant.TenantId)
                || string.IsNullOrWhiteSpace(tenant.RealmName)
                || string.IsNullOrWhiteSpace(tenant.DatabaseName)
                || tenant.CompanyId == Guid.Empty)
            {
                LogAdditionalTenantSeedIncomplete(logger, tenant.TenantId);
                continue;
            }

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", tenant.TenantId);
            command.Parameters.AddWithValue("displayName", tenant.DisplayName);
            command.Parameters.AddWithValue("adminEmail", tenant.AdminEmail);
            command.Parameters.AddWithValue("databaseName", tenant.DatabaseName);
            command.Parameters.AddWithValue("realmName", tenant.RealmName);
            command.Parameters.AddWithValue("clientSecret", (object?)tenant.ClientSecret ?? DBNull.Value);
            command.Parameters.AddWithValue("companyId", tenant.CompanyId);

            var insertedAdditional = await command.ExecuteNonQueryAsync();
            if (insertedAdditional > 0)
            {
                LogAdditionalTenantSeeded(logger, tenant.TenantId, tenant.CompanyId);
            }
            else
            {
                LogDevTenantAlreadyPresent(logger, tenant.TenantId);
            }

            // La base du tenant additionnel doit EXISTER pour que MigrateExistingTenantsAsync la migre
            // juste après (InitializeDataAsync) ; sinon le runtime, qui DÉRIVE le nom {DatabasePrefix}{id}
            // via TenantAwareNpgsqlConnectionFactory, plante toute requête en 3D000 (finding F3/RLF01).
            // Exécuté même si la ligne de registre préexistait : la base a pu manquer indépendamment.
            await EnsureTenantDatabaseExistsAsync(connection, tenant.DatabaseName, tenant.TenantId, logger);
        }
    }

    /// <summary>
    /// Crée la base d'un tenant additionnel de DEV si elle n'existe pas encore, pour que
    /// <c>MigrateExistingTenantsAsync</c> (appelé juste après dans <c>InitializeDataAsync</c>) puisse la
    /// migrer. Le nom DOIT être celui que le runtime dérive (<c>{DatabasePrefix}{tenantId}</c>, cf.
    /// <see cref="Stratum.Common.Infrastructure.Database.TenantAwareNpgsqlConnectionFactory"/>) et que le
    /// seed pose dans <c>outbox.tenants.database_name</c> — cohérence registre ↔ runtime (RLF01). Idempotent
    /// (vérifie <c>pg_database</c>) et strictement NON FATAL : un échec (rôle sans CREATEDB, etc.) est
    /// journalisé et n'empêche jamais le démarrage de dev.
    /// </summary>
    private static async Task EnsureTenantDatabaseExistsAsync(
        NpgsqlConnection connection,
        string databaseName,
        string tenantId,
        ILogger logger)
    {
        // CREATE DATABASE ne peut être ni paramétré ni exécuté dans une transaction : l'identifiant est
        // validé contre une liste blanche stricte AVANT toute interpolation (anti-injection, défense 1/2).
        if (!IsSafeDatabaseIdentifier(databaseName))
        {
            LogAdditionalTenantDatabaseNameUnsafe(logger, databaseName, tenantId);
            return;
        }

        try
        {
            await using (var existsCommand = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = @name", connection))
            {
                existsCommand.Parameters.AddWithValue("name", databaseName);
                if (await existsCommand.ExecuteScalarAsync() is not null)
                {
                    return;
                }
            }

            // Identifiant déjà validé ci-dessus ; quoté en plus (double garde, défense 2/2).
            var quoted = "\"" + databaseName.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
            await using var createCommand = new NpgsqlCommand($"CREATE DATABASE {quoted}", connection);
            await createCommand.ExecuteNonQueryAsync();
            LogAdditionalTenantDatabaseCreated(logger, databaseName, tenantId);
        }
        catch (Exception ex)
        {
            // NON FATAL : l'amorçage de dev ne doit jamais empêcher le démarrage (la base restera
            // injoignable, mais l'opérateur le voit dans les logs au lieu d'un crash de boot).
            LogAdditionalTenantDatabaseCreationFailed(logger, databaseName, tenantId, ex);
        }
    }

    // \z (et non $) : en .NET, $ matche aussi AVANT un saut de ligne final — « stratum_tenant2\n »
    // passerait la liste blanche. \z ancre la VRAIE fin de chaîne, fermant complètement le filtre.
    [GeneratedRegex(@"^[a-z][a-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex DatabaseIdentifierRegex();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seed du tenant de dev « {TenantId} » ignoré : RealmName et DatabaseName sont requis (section DevTenantSeed).")]
    private static partial void LogDevTenantSeedIncomplete(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publication de dev ignorée : date de début « {StartDate} » invalide (format attendu yyyy-MM-dd) pour « {TenantId} ».")]
    private static partial void LogDevPublicationBadDate(ILogger logger, string startDate, string tenantId);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Table de mapping TVA rattrapée pour « {TenantId} » (profil déjà présent mais table absente — seed partiel récupéré, FIX203a).")]
    private static partial void LogDevMappingBackfilled(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Paramétrage de dev amorcé pour « {TenantId} » (fiscal={FiscalImported}, comptes PA={PaAccounts}). Identité légale jamais seedée (BUG-14).")]
    private static partial void LogDevProfileSeeded(ILogger logger, string tenantId, bool fiscalImported, int paAccounts);

    [LoggerMessage(Level = LogLevel.Information, Message = "Identité légale FICTIVE du tenant de dev « {TenantId} » posée programmatiquement (hors seed — BUG-14) ; le pipeline de dev est fonctionnel.")]
    private static partial void LogDevProfileIdentitySeeded(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Identité légale du tenant de dev « {TenantId} » non amorcée (section DevTenantSeed:Profile absente ou SIREN vide) : le pipeline restera suspendu jusqu'à une saisie via la console (CFG02).")]
    private static partial void LogDevProfileIdentitySkipped(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec de l'amorçage du profil de dev pour « {TenantId} » (non fatal — le démarrage continue).")]
    private static partial void LogDevProfileSeedFailed(ILogger logger, string tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tenant de dev « {TenantId} » amorcé dans outbox.tenants (realm « {RealmName} »).")]
    private static partial void LogDevTenantSeeded(ILogger logger, string tenantId, string realmName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tenant de dev « {TenantId} » déjà présent — seed ignoré.")]
    private static partial void LogDevTenantAlreadyPresent(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tenant de recette additionnel « {TenantId} » ignoré : TenantId, RealmName, DatabaseName et un CompanyId non vide sont requis (section DevTenantSeed:AdditionalTenants).")]
    private static partial void LogAdditionalTenantSeedIncomplete(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tenant de recette additionnel « {TenantId} » amorcé dans outbox.tenants (company_id {CompanyId}).")]
    private static partial void LogAdditionalTenantSeeded(ILogger logger, string tenantId, Guid companyId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Base du tenant de recette « {TenantId} » créée : « {DatabaseName} » (sera migrée par MigrateExistingTenantsAsync).")]
    private static partial void LogAdditionalTenantDatabaseCreated(ILogger logger, string databaseName, string tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Création de la base du tenant de recette « {TenantId} » ignorée : nom « {DatabaseName} » non conforme (minuscules/chiffres/underscore, débutant par une lettre).")]
    private static partial void LogAdditionalTenantDatabaseNameUnsafe(ILogger logger, string databaseName, string tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Échec de la création de la base « {DatabaseName} » du tenant de recette « {TenantId} » (non fatal — le démarrage continue ; la base restera injoignable).")]
    private static partial void LogAdditionalTenantDatabaseCreationFailed(ILogger logger, string databaseName, string tenantId, Exception exception);
}
