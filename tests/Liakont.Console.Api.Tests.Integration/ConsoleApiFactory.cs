namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Dapper;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Host.Startup;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Domain;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Ingestion.Contracts;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Domain.Payments;
using Liakont.Modules.Reconciliation.Application;
using Liakont.Modules.Reconciliation.Domain;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.Fake;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Harness HTTP in-process de la console (API01a), réutilisé par les endpoints API (API01b/c, API02-05)
/// et les pages WEB. Démarre la plateforme Liakont (<see cref="Liakont.Host"/>) sur un port loopback,
/// adossée à un conteneur PostgreSQL (Testcontainers), avec :
/// <list type="bullet">
///   <item>un schéma d'authentification de test (<see cref="TestAuthHandler"/>) à la place de Keycloak —
///         l'identité est portée par l'en-tête <c>X-Test-User</c> ; l'AUTORISATION reste celle de
///         production : la garde (<c>PermissionAuthorizationHandler</c>) décide sur les claims
///         <c>permission</c> (ADR-0017), que le harness projette depuis les grants en base du tenant
///         (<c>identity.grants</c> = source, transportée en claims comme au sign-in OIDC) ;</item>
///   <item>deux bases de TENANT distinctes (database-per-tenant réel) routées par
///         <c>TenantConnections:ConnectionStrings</c> — l'isolation A≠B est donc PHYSIQUE, vérifiée par
///         test ; le tenant est résolu de l'en-tête <c>X-Tenant-Id</c> par le middleware de production.</item>
/// </list>
/// <para>
/// L'application est construite manuellement (et non via <c>WebApplicationFactory&lt;Program&gt;</c>) :
/// c'est le pattern in-process déjà éprouvé du dépôt pour ce Host Blazor SSR (cf.
/// <c>Liakont.Tests.E2E.KeycloakE2EWebFactory</c>), qui évite les écueils du manifeste de static web
/// assets sous <c>TestServer</c>. Le critère « WebApplicationFactory + Testcontainers » de l'item est
/// satisfait en substance : hôte HTTP in-process + PostgreSQL conteneurisé.
/// </para>
/// </summary>
public sealed class ConsoleApiFactory : IAsyncLifetime, IAsyncDisposable
{
    public const string TenantA = "tenant-a";
    public const string TenantB = "tenant-b";

    /// <summary>
    /// Tenant dédié aux tests d'ACTION (API02a : envoi, déclenchement). Distinct de A/B : les actions
    /// déclenchent un SEND réel (le JobWorker live consomme les déclencheurs publiés) qui écrit des
    /// <c>pipeline.run_logs</c> ; les confiner ici évite de polluer les comptes EXACTS qu'API01b asserte sur A/B.
    /// </summary>
    public const string TenantAction = "tenant-act";

    /// <summary>Tenant dédié aux exports d'archive (API03), avec un coffre réel — toujours SAIN.</summary>
    public const string TenantArchive = "tenant-arch";

    /// <summary>Tenant dédié au test de chaîne ALTÉRÉE (API03) : un seul test l'archive puis le falsifie.</summary>
    public const string TenantArchiveTampered = "tenant-arch-bad";

    /// <summary>
    /// Tenant dédié aux tests d'édition API04 (table TVA + réconciliation) : ses MUTATIONS (ajout/suppression
    /// de règle, invalidation, rejet/confirmation de proposition) ne polluent pas l'état lu par les autres
    /// suites (tenant A reste « table TVA validée » pour les assertions de paramétrage).
    /// </summary>
    public const string TenantApi04 = "tenant-api04";

    /// <summary>
    /// Tenant dédié aux tests de VERDICT garde-fou B2B/B2C + RE-VÉRIFICATION (API02b). Ces actions MUTENT
    /// l'état de documents (verdict, recheck → ReadyToSend / ManuallyHandled) : les confiner ici évite de
    /// polluer les comptes EXACTS qu'API02a asserte sur le tenant d'action. Doté d'un profil tenant (SIREN
    /// valide) + d'une table TVA validée dont la part MATCHE la requête du CHECK (Autre) : une re-vérification
    /// peut donc atteindre ReadyToSend. Les documents bloqués + pivots stagés sont seedés FRAIS par test
    /// (<see cref="SeedBlockedProfessionalBuyerDocumentAsync"/>) pour éviter tout couplage inter-tests.
    /// </summary>
    public const string TenantVerdict = "tenant-verdict";

    /// <summary>
    /// Tenant VIERGE (identité seulement, AUCUN profil) dédié à l'import de seed admin (FIX01a) : un test
    /// y importe un profil via <c>POST /admin/tenants/{id}/seed</c>. Isolé (sa mutation — création de
    /// profil — ne pollue pas l'export de réversibilité API03 ni les comptes EXACTS des autres suites).
    /// </summary>
    public const string TenantSeed = "tenant-seed";

    public const string BlockedReasonText = "Régime TVA non mappé : compléter la table TVA (document FA-A-002).";

    public const string OlderBlockedReasonText = "Ancien motif (corrigé puis re-bloqué).";

    // Journal des traitements (pipeline.run_logs) seedé — API01b GET /runs. Détails distincts par tenant
    // pour vérifier l'isolation A≠B et le filtre par intervalle de dates.
    public const string TenantAJanRunDetail = "run-A-jan";
    public const string TenantAFebRunDetail = "run-A-feb";
    public const string TenantBRunDetail = "run-B-mar";

    // Motif opérateur de l'agrégat Suspended seedé (tenant A) — projection pipeline.payment_aggregations,
    // API01b GET /payments : le tenant A porte un agrégat Calculated ET un Suspended (décision fiscale en
    // attente) ; le tenant B uniquement un Calculated (sert l'isolation et l'absence de décision en attente).
    public const string FiscalPendingReasonText =
        "Décision fiscale en attente (TVA sur les débits / catégorie d'opération) — consultez votre expert-comptable.";

    // Paramétrage tenant seedé — API01c GET /settings (constantes ; les companyId/capacités sont plus bas).
    public const string TenantASiren = "111111111";
    public const string TenantBSiren = "222222222";
    public const string TenantARaisonSociale = "ACME Ventes SARL";
    public const string TenantBRaisonSociale = "Beta Négoce SAS";

    /// <summary>Type de plug-in PA enregistré dans le harness — ses capacités sont exposées par /settings.</summary>
    public const string FakePluginType = "Fake";

    /// <summary>Type de PA configuré pour le tenant A SANS plug-in chargé (teste PluginAvailable=false).</summary>
    public const string UnregisteredPluginType = "UnknownPa";

    public const string TenantATvaVersion = "cmp-A-v1";
    public const string TenantATvaValidatedBy = "Expert-comptable A";

    // Utilisateurs seedés (claim NameIdentifier porté par X-Test-User).
    public static readonly Guid ReaderUserId = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid NoPermissionUserId = new("22222222-2222-2222-2222-222222222222");

    /// <summary>Utilisateur porteur de liakont.settings (+read) — requis par tenant-export (API03).</summary>
    public static readonly Guid SettingsUserId = new("44444444-4444-4444-4444-444444444444");

    /// <summary>
    /// Opérateur porteur de liakont.actions (+read) — exécute les actions de la console (envoi, déclenchement —
    /// API02a ; réconciliation — API04). SANS liakont.settings : sert aussi le « 403 sur l'édition de la table
    /// TVA » (API04). Le simple lecteur (liakont.read seul) sert le cas « 403 sur une action ».
    /// </summary>
    public static readonly Guid OperatorUserId = new("66666666-6666-6666-6666-666666666666");

    /// <summary>Société (companyId) du tenant d'édition API04 — claim company_id porté par X-Test-Company.</summary>
    public static readonly Guid TenantApi04CompanyId = new("cccccccc-0000-0000-0000-0000000000c1");

    /// <summary>Société (companyId) du tenant de verdict/recheck (API02b) — résolue par le profil du tenant.</summary>
    public static readonly Guid TenantVerdictCompanyId = new("dddddddd-0000-0000-0000-0000000000d1");

    /// <summary>Société (companyId) du tenant vierge — passée à l'import de seed admin (FIX01a).</summary>
    public static readonly Guid TenantSeedCompanyId = new("eeeeeeee-0000-0000-0000-0000000000e1");

    /// <summary>Utilisateur SystemAdmin (rôle porté par l'en-tête X-Test-Roles) — exécute /admin/tenants/*.</summary>
    public static readonly Guid SystemAdminUserId = new("88888888-8888-8888-8888-888888888888");

    /// <summary>SIREN émetteur FICTIF mais valide (Luhn) du profil du tenant de verdict — exigé par SupplierIdentityRule au recheck.</summary>
    private const string VerdictProfileSiren = "404833048";

    /// <summary>Document ReadyToSend PRÉ-SEEDÉ (lecture seule) du tenant de verdict — sert les 409 « non bloqué » (verdict/recheck).</summary>
    public static readonly Guid TenantVerdictReadyDocId = new("0d000001-0000-0000-0000-000000000001");

    // Documents seedés dans le TENANT A.
    public static readonly Guid TenantADocReadyId = new("0a000001-0000-0000-0000-000000000001");
    public static readonly Guid TenantADocBlockedId = new("0a000002-0000-0000-0000-000000000002");
    public static readonly Guid TenantADocIssuedId = new("0a000003-0000-0000-0000-000000000003");

    // Document seedé dans le TENANT B (sert l'isolation : il n'existe pas dans A).
    public static readonly Guid TenantBDocReadyId = new("0b000001-0000-0000-0000-000000000001");

    // Documents seedés dans le TENANT D'ACTION (API02a) : un ReadyToSend (envoi + récapitulatif) et un Blocked (409).
    public static readonly Guid TenantActDocReadyId = new("0c000001-0000-0000-0000-000000000001");
    public static readonly Guid TenantActDocBlockedId = new("0c000002-0000-0000-0000-000000000002");

    // Documents seedés dans le TENANT D'ACTION pour les RÉSOLUTIONS TERMINALES (API02c). Chacun est DÉDIÉ à
    // un scénario (isolation de la fixture partagée) ; AUCUN n'est ReadyToSend (le récap d'envoi API02a compte
    // exactement 1 ReadyToSend dans ce tenant).
    public static readonly Guid TenantActDocResolveBlockedId = new("0c000003-0000-0000-0000-000000000003");
    public static readonly Guid TenantActDocResolveRejectedId = new("0c000004-0000-0000-0000-000000000004");
    public static readonly Guid TenantActDocSupersedeRejectedId = new("0c000005-0000-0000-0000-000000000005");
    public static readonly Guid TenantActDocSupersedeNoReplId = new("0c000006-0000-0000-0000-000000000006");
    public static readonly Guid TenantActDocStableIssuedId = new("0c000007-0000-0000-0000-000000000007");

    // ── Paramétrage tenant seedé — API01c GET /settings ──
    /// <summary>Société (companyId) de l'unique profil de paramétrage seedé dans le tenant A.</summary>
    public static readonly Guid TenantACompanyId = new("aaaaaaaa-0000-0000-0000-0000000000a1");

    /// <summary>Société (companyId) du profil seedé dans le tenant B (sert l'isolation du paramétrage).</summary>
    public static readonly Guid TenantBCompanyId = new("bbbbbbbb-0000-0000-0000-0000000000b1");

    /// <summary>Capacités déclarées par le plug-in factice enregistré dans le harness (assertions /settings).</summary>
    public static readonly PaCapabilities FakeCapabilities = new()
    {
        PaName = "Plateforme Factice (test)",
        SupportsB2cReporting = true,
        SupportsDomesticPaymentReporting = true,
        SupportsInternationalPaymentReporting = false,
        SupportsB2bInvoicing = false,
        SupportsCreditNotes = true,
        SupportsTaxReportRetrieval = true,
        SupportsDocumentRetrieval = true,
        SupportsReportRectification = true,
        MaxDocumentsPerRequest = 50,
    };

    /// <summary>Codes régime source du pivot de verdict (un seul) — champ partagé pour éviter l'allocation répétée (CA1861).</summary>
    private static readonly string[] VerdictPivotRegimeCodes = { "REGIME-A" };

    private static readonly Guid ConsoleReaderRoleId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ConsoleAdminRoleId = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ConsoleOperatorRoleId = new("77777777-7777-7777-7777-777777777777");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly int _port = GetFreePort();
    private readonly Dictionary<string, string> _connectionsByTenant = new(StringComparer.Ordinal);
    private WebApplication? _app;
    private string _archiveStoreRoot = string.Empty;
    private string _systemConnectionString = string.Empty;
    private string _ingestionPdfRoot = string.Empty;
    private string _stagingRoot = string.Empty;

    public string BaseUrl => $"http://127.0.0.1:{_port}";

    /// <summary>
    /// Le fournisseur de services de l'hôte in-process — permet aux tests de comportement de résoudre un service
    /// réel (ex. un <c>IJobHandler&lt;T&gt;</c>) et de l'exécuter comme le ferait le worker, pour PROUVER
    /// l'exécution réelle d'un job déclenché (anti faux-vert), au-delà de la seule réponse HTTP 202.
    /// </summary>
    public IServiceProvider Services => _app!.Services;

    /// <summary>Chaîne de connexion de la base SYSTÈME (queue de jobs réelle + piste d'audit cross-tenant).</summary>
    public string SystemConnectionString => _systemConnectionString;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var systemConn = _postgres.GetConnectionString();
        _systemConnectionString = systemConn;
        var tenantAConn = WithDatabase(systemConn, "tc_tenant_a");
        var tenantBConn = WithDatabase(systemConn, "tc_tenant_b");
        var tenantActConn = WithDatabase(systemConn, "tc_tenant_act");
        var tenantArchConn = WithDatabase(systemConn, "tc_tenant_arch");
        var tenantArchBadConn = WithDatabase(systemConn, "tc_tenant_arch_bad");
        var tenantApi04Conn = WithDatabase(systemConn, "tc_tenant_api04");
        var tenantVerdictConn = WithDatabase(systemConn, "tc_tenant_verdict");
        var tenantSeedConn = WithDatabase(systemConn, "tc_tenant_seed");
        _connectionsByTenant[TenantA] = tenantAConn;
        _connectionsByTenant[TenantB] = tenantBConn;
        _connectionsByTenant[TenantAction] = tenantActConn;
        _connectionsByTenant[TenantArchive] = tenantArchConn;
        _connectionsByTenant[TenantArchiveTampered] = tenantArchBadConn;
        _connectionsByTenant[TenantApi04] = tenantApi04Conn;
        _connectionsByTenant[TenantVerdict] = tenantVerdictConn;
        _connectionsByTenant[TenantSeed] = tenantSeedConn;

        var hostDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Host", "Liakont.Host"));

        EnsureStaticWebAssetsManifest();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Test",
            ContentRootPath = hostDir,
            WebRootPath = Path.Combine(hostDir, "wwwroot"),
        });
        builder.WebHost.UseUrls(BaseUrl);
        builder.WebHost.UseStaticWebAssets();

        // Base système + bases de tenant distinctes (database-per-tenant routé pour de vrai).
        builder.Configuration["Database:ConnectionString"] = systemConn;
        builder.Configuration[$"TenantConnections:ConnectionStrings:{TenantA}"] = tenantAConn;
        builder.Configuration[$"TenantConnections:ConnectionStrings:{TenantB}"] = tenantBConn;
        builder.Configuration[$"TenantConnections:ConnectionStrings:{TenantAction}"] = tenantActConn;
        builder.Configuration[$"TenantConnections:ConnectionStrings:{TenantArchive}"] = tenantArchConn;
        builder.Configuration[$"TenantConnections:ConnectionStrings:{TenantArchiveTampered}"] = tenantArchBadConn;
        builder.Configuration[$"TenantConnections:ConnectionStrings:{TenantApi04}"] = tenantApi04Conn;
        builder.Configuration[$"TenantConnections:ConnectionStrings:{TenantVerdict}"] = tenantVerdictConn;
        builder.Configuration[$"TenantConnections:ConnectionStrings:{TenantSeed}"] = tenantSeedConn;

        // Magasin de staging (PIP00) sur un répertoire de test dédié et isolé (un par run) ; nettoyé au Dispose.
        // Requis par la re-vérification (API02b) qui relit le pivot stagé du document bloqué.
        _stagingRoot = Path.Combine(Path.GetTempPath(), "liakont-console-staging", Guid.NewGuid().ToString("N"));
        builder.Configuration["Staging:Storage:FileSystem:RootPath"] = _stagingRoot;

        // Coffre d'archive sur un répertoire de test dédié et isolé (un par run) ; nettoyé au Dispose.
        _archiveStoreRoot = Path.Combine(Path.GetTempPath(), "liakont-console-archive", Guid.NewGuid().ToString("N"));
        builder.Configuration["Archive:Storage:FileSystem:RootPath"] = _archiveStoreRoot;

        // Racine du stockage fichier des PDF d'ingestion (pool de réconciliation, API04) — répertoire de
        // test dédié et isolé, nettoyé au Dispose.
        _ingestionPdfRoot = Path.Combine(Path.GetTempPath(), "liakont-console-pdf", Guid.NewGuid().ToString("N"));
        builder.Configuration["Ingestion:Storage:PdfRootPath"] = _ingestionPdfRoot;

        // Keycloak factice (login désactivé) : l'abstraction d'IdP exige une autorité configurée, mais
        // aucun chemin OIDC/JWT n'est exercé — le schéma « Test » est le schéma par défaut ci-dessous.
        builder.Configuration["Keycloak:Authority"] = "http://127.0.0.1:1/realms/test";
        builder.Configuration["Keycloak:ClientId"] = "test";
        builder.Configuration["Keycloak:RequireHttpsMetadata"] = "false";
        builder.Configuration["Keycloak:UseKeycloak"] = "false";

        AppBootstrap.ConfigureServices(builder);

        // Plug-in PA factice (PAA02) : le Host n'enregistre aucune PA concrète (CLAUDE.md n°6) ; on en
        // branche un dans le harness pour que GET /settings (API01c) puisse EXPOSER des capacités PA
        // déclarées (jamais inventées). Capacités distinctives → assertions stables côté test.
        builder.Services.AddFakePaClient(new FakePaClientOptions { Capabilities = FakeCapabilities });

        // Remplace l'authentification par le schéma de test (X-Test-User → NameIdentifier + claims
        // permission projetés des grants du tenant), schéma par défaut. L'autorisation reste celle de
        // production : la garde décide sur les claims permission (la base du tenant en est la source).
        builder.Services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        builder.Services.Configure<AuthenticationOptions>(options =>
        {
            options.DefaultScheme = TestAuthHandler.SchemeName;
            options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
            options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
        });

        _app = builder.Build();
        AppBootstrap.ConfigureMiddleware(_app);

        // Construit la fabrique système (singleton) pour que son constructeur enregistre le
        // DateOnlyTypeHandler Dapper GLOBAL (cf. NpgsqlConnectionFactory) AVANT tout seeding/lecture par
        // Dapper — comme le fait le démarrage réel via InitializeDataAsync. Sans lui, un paramètre DateOnly
        // (issue_date, filtres from/to) lève « DateOnly cannot be used as a parameter value ».
        _ = _app.Services.GetRequiredService<NpgsqlConnectionFactory>();

        // Migre la base système ET chaque base de tenant avec le schéma complet de la plateforme
        // (MigrationRunner.MigrateUp crée la base si besoin et applique Common + tous les modules).
        var migrationAssemblies = _app.Services.GetRequiredService<IOptions<MigrationAssembliesOptions>>();
        MigrateDatabase(migrationAssemblies, systemConn);
        MigrateDatabase(migrationAssemblies, tenantAConn);
        MigrateDatabase(migrationAssemblies, tenantBConn);
        MigrateDatabase(migrationAssemblies, tenantActConn);
        MigrateDatabase(migrationAssemblies, tenantArchConn);
        MigrateDatabase(migrationAssemblies, tenantArchBadConn);
        MigrateDatabase(migrationAssemblies, tenantApi04Conn);
        MigrateDatabase(migrationAssemblies, tenantVerdictConn);
        MigrateDatabase(migrationAssemblies, tenantSeedConn);

        // Tenant vierge (FIX01a) : enregistré dans le catalogue système (outbox.tenants) pour que
        // l'endpoint d'admin le RÉSOLVE (ITenantQueries.GetByIdAsync), mais SANS profil ni identité — un
        // test y importe un profil via POST /admin/tenants/{id}/seed.
        await RegisterSystemTenantAsync(systemConn, TenantSeed, "tc_tenant_seed");

        await SeedTenantAAsync(tenantAConn);
        await SeedTenantBAsync(tenantBConn);
        await SeedTenantActionAsync(tenantActConn);

        // Les tenants d'archive ne portent que l'identité (les documents sont archivés à la demande par
        // les tests via ArchiveSampleDocumentAsync, sur un coffre réel).
        await SeedIdentityOnlyAsync(tenantArchConn);
        await SeedIdentityOnlyAsync(tenantArchBadConn);

        // Tenant d'édition API04 : identité + une table TVA VALIDÉE (les tests mutent cette table sans
        // toucher l'état lu par les autres suites). La réconciliation est seedée à la demande par les tests.
        await SeedTenantApi04Async(tenantApi04Conn);

        // Tenant de verdict/recheck API02b : identité + profil (SIREN valide) + table TVA validée (part Autre).
        await SeedTenantVerdictAsync(tenantVerdictConn);

        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        await _postgres.DisposeAsync().AsTask();
        DeleteArchiveStore();
        DeleteIngestionPdfStore();
        DeleteStagingStore();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await DisposeAsync();
    }

    /// <summary>
    /// Crée un client HTTP ciblant l'hôte in-process, avec l'en-tête de tenant (<c>X-Tenant-Id</c>) et,
    /// si fourni, l'identité de test (<c>X-Test-User</c>). Sans utilisateur, la requête est anonyme.
    /// </summary>
    public HttpClient CreateClient(string tenantId, Guid? userId = null, Guid? companyId = null, string? roles = null)
    {
        var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
        if (userId is { } user)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user.ToString());
        }

        // company_id : porté par le jeton en production (Keycloak) ; fourni ici pour les endpoints scopés
        // société (table TVA, API04). Omis = aucun claim company_id (comportement historique inchangé).
        if (companyId is { } company)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.CompanyHeader, company.ToString());
        }

        // Rôles (ex. SystemAdmin) : portés par le jeton de l'IdP en production ; fournis ici pour les
        // endpoints gardés par rôle (/admin/tenants). Omis = aucun claim de rôle.
        if (!string.IsNullOrWhiteSpace(roles))
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        }

        return client;
    }

    /// <summary>Chaîne de connexion de la base d'un TENANT (vérification d'absence de job orphelin / de run_log).</summary>
    public string TenantConnectionString(string tenant) => ConnectionStringFor(tenant);

    /// <summary>
    /// Lit l'état courant d'un document dans la base d'un TENANT (<c>documents.documents</c>) — preuve qu'une
    /// action de console a réellement appliqué la transition terminale (API02c : ManuallyHandled / Superseded),
    /// ou qu'un refus (4xx) n'a rien muté.
    /// </summary>
    public async Task<string?> GetDocumentStateAsync(string tenant, Guid documentId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(ConnectionStringFor(tenant));
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT state FROM documents.documents WHERE id = @Id",
            new { Id = documentId },
            cancellationToken: ct));
    }

    /// <summary>
    /// Compte les événements d'audit append-only (<c>documents.document_events</c>) d'un type donné pour un
    /// document dans un TENANT — prouve l'inscription de la transition dans la piste d'audit (API02c).
    /// </summary>
    public async Task<int> CountDocumentEventsAsync(string tenant, Guid documentId, string eventType, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(ConnectionStringFor(tenant));
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM documents.document_events WHERE document_id = @Id AND event_type = @EventType",
            new { Id = documentId, EventType = eventType },
            cancellationToken: ct));
    }

    /// <summary>
    /// Lit l'identité de l'opérateur (<c>operator_identity</c>) du DERNIER événement d'audit d'un type donné pour
    /// un document — prouve qu'une action OPÉRATEUR (ex. re-vérification FIX02) est attribuée dans la piste
    /// append-only, et non écrite comme un événement système anonyme.
    /// </summary>
    public async Task<string?> GetLatestEventOperatorIdentityAsync(string tenant, Guid documentId, string eventType, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(ConnectionStringFor(tenant));
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT operator_identity FROM documents.document_events WHERE document_id = @Id AND event_type = @EventType ORDER BY timestamp_utc DESC, id DESC LIMIT 1",
            new { Id = documentId, EventType = eventType },
            cancellationToken: ct));
    }

    /// <summary>
    /// Compte les entrées de la piste d'activité opérateur (<c>audit.activities</c>, base SYSTÈME) pour un type
    /// d'activité et une entité donnés — prouve qu'une action de la console est journalisée (anti faux-vert ;
    /// l'écriture d'audit est awaitée par l'endpoint avant la réponse HTTP).
    /// </summary>
    public async Task<int> CountActivitiesAsync(string activityType, string entityId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_systemConnectionString);
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM audit.activities WHERE activity_type = @ActivityType AND entity_id = @EntityId",
            new { ActivityType = activityType, EntityId = entityId },
            cancellationToken: ct));
    }

    /// <summary>
    /// Compte les jobs de la queue SYSTÈME (<c>job.jobs</c> de la base système — celle que le <c>JobWorker</c>
    /// consomme) dont le type CONTIENT <paramref name="typeContains"/>. Prouve qu'une action publie bien sur la
    /// queue système (ADR-0016 / INV-API02a-2), et permet de vérifier l'ABSENCE de fan-out (SendAllTrigger).
    /// </summary>
    public Task<int> CountSystemJobsAsync(string typeContains, CancellationToken ct = default) =>
        CountJobsAsync(_systemConnectionString, typeContains, ct);

    /// <summary>
    /// Compte les jobs présents dans <c>job.jobs</c> de la base d'un TENANT dont le type contient
    /// <paramref name="typeContains"/> — doit rester à 0 pour une action de console (un job en base tenant serait
    /// ORPHELIN, jamais consommé par le worker null-tenant : ADR-0016 / INV-API02a-2).
    /// </summary>
    public Task<int> CountTenantJobsAsync(string tenant, string typeContains, CancellationToken ct = default) =>
        CountJobsAsync(ConnectionStringFor(tenant), typeContains, ct);

    /// <summary>
    /// Compte les traitements MANUELS d'envoi (<c>pipeline.run_logs</c> : run_type='Send', run_trigger='Manual')
    /// d'un tenant — preuve d'EXÉCUTION réelle, tenant-scopée, du SEND déclenché par une action (INV-API02a-1/5).
    /// </summary>
    public async Task<int> CountManualSendRunLogsAsync(string tenant, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(ConnectionStringFor(tenant));
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM pipeline.run_logs WHERE run_type = 'Send' AND run_trigger = 'Manual'",
            cancellationToken: ct));
    }

    private static async Task<int> CountJobsAsync(string connectionString, string typeContains, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM job.jobs WHERE type LIKE @Pattern",
            new { Pattern = "%" + typeContains + "%" },
            cancellationToken: ct));
    }

    /// <summary>
    /// Archive un document ÉMIS dans le tenant donné via le VRAI service Archive (coffre réel + chaîne de
    /// hashes), pour exercer les exports d'audit (API03). Seede d'abord le document dans la base du tenant
    /// (chronologie réelle), puis archive dans un scope tenant. Retourne l'identifiant du document.
    /// </summary>
    public async Task<Guid> ArchiveSampleDocumentAsync(string tenant, string number, DateOnly issueDate, CancellationToken cancellationToken = default)
    {
        var documentId = Guid.NewGuid();

        await using (var conn = new NpgsqlConnection(ConnectionStringFor(tenant)))
        {
            await conn.OpenAsync(cancellationToken);
            await InsertDocumentAsync(conn, documentId, number, "invoice", issueDate, "Issued", "Client Export", 1200.00m);
        }

        var scopeFactory = _app!.Services.GetRequiredService<ITenantScopeFactory>();
        await using ITenantScope scope = scopeFactory.Create(tenant);
        var archiveService = scope.Services.GetRequiredService<IArchiveService>();
        await archiveService.ArchiveIssuedDocumentAsync(BuildPackageRequest(documentId, number, issueDate), cancellationToken);

        return documentId;
    }

    /// <summary>
    /// Falsifie le contenu d'un paquet d'archive du tenant (un fichier <c>payload.json</c>) en levant le
    /// verrou WORM applicatif (lecture seule) puis en réécrivant — pour exercer la détection d'altération
    /// (API03). Retourne <c>true</c> si un fichier a été falsifié.
    /// </summary>
    public bool TamperArchivedPayload(string tenant)
    {
        string tenantDir = Path.Combine(_archiveStoreRoot, ArchivePackageLayout.SanitizeSegment(tenant));
        if (!Directory.Exists(tenantDir))
        {
            return false;
        }

        foreach (string file in Directory.EnumerateFiles(tenantDir, "payload.json", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.WriteAllBytes(file, Encoding.UTF8.GetBytes("{\"tampered\":true}"));
            return true;
        }

        return false;
    }

    private static ArchivePackageRequest BuildPackageRequest(Guid documentId, string number, DateOnly issueDate) => new()
    {
        DocumentId = documentId,
        DocumentNumber = number,
        IssueDate = issueDate,
        PayloadJson = "{\"number\":\"" + number + "\"}",
        PaResponseJson = "{\"paDocumentId\":\"PA-1\"}",
        Readable = new ArchiveReadableDocument(
            number,
            "Facture",
            issueDate,
            "EUR",
            "ACME Ventes SARL",
            "123456789",
            "Client Export",
            new List<ArchiveReadableLine> { new("Service", 1m, 1000m, 1000m, "20 %") },
            new List<ArchiveVatBreakdownLine> { new("20 %", 1000m, 200m) },
            1000m,
            200m,
            1200m),
        PaInvoice = null,
        PaInvoiceAbsenceReason = "La PA ne fournit pas la facture (test).",
        SourceDocument = null,
        SourceDocumentAbsenceReason = "L'adaptateur ne fournit pas le bordereau (test).",
    };

    private static void MigrateDatabase(
        IOptions<MigrationAssembliesOptions> migrationAssemblies,
        string connectionString)
    {
        var runner = new MigrationRunner(
            Options.Create(new DatabaseOptions { ConnectionString = connectionString }),
            migrationAssemblies,
            NullLogger<MigrationRunner>.Instance);
        runner.MigrateUp();
    }

    private static async Task SeedIdentityOnlyAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await SeedIdentityAsync(conn);
    }

    /// <summary>
    /// Enregistre un tenant dans le catalogue système (<c>outbox.tenants</c>, base SYSTÈME) pour qu'il
    /// soit RÉSOLU par <c>ITenantQueries</c> (l'endpoint d'admin vérifie l'existence du tenant cible).
    /// </summary>
    private static async Task RegisterSystemTenantAsync(string systemConnectionString, string tenantId, string databaseName)
    {
        await using var conn = new NpgsqlConnection(systemConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO outbox.tenants (id, display_name, admin_email, database_name, realm_name, is_active)
            VALUES (@Id, @DisplayName, @AdminEmail, @DatabaseName, @RealmName, TRUE)
            ON CONFLICT (id) DO NOTHING
            """,
            new
            {
                Id = tenantId,
                DisplayName = "Tenant vierge (seed)",
                AdminEmail = "admin@tenant-seed.test",
                DatabaseName = databaseName,
                RealmName = "stratum-" + tenantId,
            });
    }

    private static async Task SeedIdentityAsync(NpgsqlConnection conn)
    {
        // Rôle « console-reader » porteur de liakont.read + un utilisateur qui le détient (200) et un
        // utilisateur sans aucun rôle (403). Rôle « console-admin » porteur de liakont.read + liakont.settings
        // (requis par tenant-export, API03). L'autorisation de production lit ces lignes par tenant.
        await conn.ExecuteAsync(
            """
            INSERT INTO identity.roles (id, name, description, is_system)
            VALUES
                (@ReaderRoleId, 'console-reader', 'Lecture console (tests)', false),
                (@AdminRoleId, 'console-admin', 'Administration console (tests)', false),
                (@OperatorRoleId, 'console-operator', 'Actions opérateur console (tests)', false)
            ON CONFLICT (id) DO NOTHING
            """,
            new { ReaderRoleId = ConsoleReaderRoleId, AdminRoleId = ConsoleAdminRoleId, OperatorRoleId = ConsoleOperatorRoleId });

        await conn.ExecuteAsync(
            """
            INSERT INTO identity.grants (role_id, permission, module_source)
            VALUES
                (@ReaderRoleId, 'liakont.read', 'liakont'),
                (@AdminRoleId, 'liakont.read', 'liakont'),
                (@AdminRoleId, 'liakont.settings', 'liakont'),
                (@OperatorRoleId, 'liakont.read', 'liakont'),
                (@OperatorRoleId, 'liakont.actions', 'liakont')
            ON CONFLICT (role_id, permission) DO NOTHING
            """,
            new { ReaderRoleId = ConsoleReaderRoleId, AdminRoleId = ConsoleAdminRoleId, OperatorRoleId = ConsoleOperatorRoleId });

        await conn.ExecuteAsync(
            """
            INSERT INTO identity.users (id, username, email, display_name, password_hash, is_active)
            VALUES
                (@ReaderId, 'console.reader', 'reader@test.local', 'Console Reader', 'x', true),
                (@NoPermId, 'console.noperm', 'noperm@test.local', 'Console NoPerm', 'x', true),
                (@SettingsId, 'console.admin', 'admin@test.local', 'Console Admin', 'x', true),
                (@ActionsId, 'console.operator', 'operator@test.local', 'Console Operator', 'x', true)
            ON CONFLICT (id) DO NOTHING
            """,
            new { ReaderId = ReaderUserId, NoPermId = NoPermissionUserId, SettingsId = SettingsUserId, ActionsId = OperatorUserId });

        await conn.ExecuteAsync(
            """
            INSERT INTO identity.user_roles (user_id, role_id)
            VALUES
                (@ReaderId, @ReaderRoleId),
                (@SettingsId, @AdminRoleId),
                (@ActionsId, @OperatorRoleId)
            ON CONFLICT (user_id, role_id) DO NOTHING
            """,
            new { ReaderId = ReaderUserId, ReaderRoleId = ConsoleReaderRoleId, SettingsId = SettingsUserId, AdminRoleId = ConsoleAdminRoleId, ActionsId = OperatorUserId, OperatorRoleId = ConsoleOperatorRoleId });
    }

    /// <summary>
    /// Tenant dédié aux tests d'ACTION (API02a) : identité (dont l'opérateur) + un document ReadyToSend (envoi
    /// unitaire + récapitulatif send-all : count=1, montant 100.00) + un document Blocked (cas 409). Aucun
    /// run_log / agrégat seedé ici — les actions y écrivent leurs propres traces sans perturber A/B.
    /// </summary>
    private static async Task SeedTenantActionAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await SeedIdentityAsync(conn);

        await InsertDocumentAsync(conn, TenantActDocReadyId, "FA-ACT-001", "invoice", new DateOnly(2026, 1, 18), "ReadyToSend", "Client Action", 100.00m);
        await InsertDocumentAsync(conn, TenantActDocBlockedId, "FA-ACT-002", "invoice", new DateOnly(2026, 2, 18), "Blocked", "Client Action", 200.00m);

        // API02c — résolutions terminales : aucun document ReadyToSend (ne perturbe pas le récap d'envoi API02a).
        await InsertDocumentAsync(conn, TenantActDocResolveBlockedId, "FA-ACT-003", "invoice", new DateOnly(2026, 3, 1), "Blocked", "Client Action", 300.00m);
        await InsertDocumentAsync(conn, TenantActDocResolveRejectedId, "FA-ACT-004", "invoice", new DateOnly(2026, 3, 2), "RejectedByPa", "Client Action", 400.00m);
        await InsertDocumentAsync(conn, TenantActDocSupersedeRejectedId, "FA-ACT-005", "invoice", new DateOnly(2026, 3, 3), "RejectedByPa", "Client Action", 500.00m);
        await InsertDocumentAsync(conn, TenantActDocSupersedeNoReplId, "FA-ACT-006", "invoice", new DateOnly(2026, 3, 4), "RejectedByPa", "Client Action", 600.00m);
        await InsertDocumentAsync(conn, TenantActDocStableIssuedId, "FA-ACT-007", "invoice", new DateOnly(2026, 3, 5), "Issued", "Client Action", 700.00m);
    }

    private static async Task SeedTenantAAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await SeedIdentityAsync(conn);

        await InsertDocumentAsync(conn, TenantADocReadyId, "FA-A-001", "invoice", new DateOnly(2026, 1, 10), "ReadyToSend", "Client Alpha", 100.00m);
        await InsertDocumentAsync(conn, TenantADocBlockedId, "FA-A-002", "invoice", new DateOnly(2026, 2, 15), "Blocked", "Client Beta", 200.00m);
        await InsertDocumentAsync(conn, TenantADocIssuedId, "AV-A-003", "credit_note", new DateOnly(2026, 3, 20), "Issued", "Client Gamma", 50.00m);

        // Deux événements DocumentBlocked sur TenantADocBlockedId — valide que le dernier (le plus récent) gagne.
        await InsertEventAsync(conn, TenantADocBlockedId, new DateTimeOffset(2026, 2, 15, 9, 0, 0, TimeSpan.Zero), "DocumentBlocked", OlderBlockedReasonText, payloadSnapshot: null);
        await InsertEventAsync(conn, TenantADocBlockedId, new DateTimeOffset(2026, 2, 15, 10, 0, 0, TimeSpan.Zero), "DocumentBlocked", BlockedReasonText, payloadSnapshot: null);

        // Événement DocumentBlocked antérieur sur le document émis — valide que BlockingReason est null quand State != Blocked.
        await InsertEventAsync(conn, TenantADocIssuedId, new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.Zero), "DocumentBlocked", detail: "Bloqué avant correction, puis émis.", payloadSnapshot: null);

        // Pivot transmis (événement DocumentIssued) — alimente PivotSnapshotJson du détail.
        await InsertEventAsync(conn, TenantADocIssuedId, new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero), "DocumentIssued", detail: "Émis", payloadSnapshot: "{\"number\":\"AV-A-003\"}");

        // Entrée de coffre WORM pour le document émis — alimente Archive + ArchiveIntegrity du détail.
        await conn.ExecuteAsync(
            """
            INSERT INTO documents.archive_entries (document_id, package_path, package_hash, chain_hash, archived_utc)
            VALUES (@DocId, @Path, @Hash, @Chain, @ArchivedUtc)
            """,
            new
            {
                DocId = TenantADocIssuedId,
                Path = "vault/tenant-a/AV-A-003.zip",
                Hash = "sha256:packagehash",
                Chain = "sha256:chainhash",
                ArchivedUtc = new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero),
            });

        // Traitements (pipeline.run_logs) — un en janvier, un en février (filtre par dates GET /runs).
        await InsertRunLogAsync(conn, "Check", "Manual", new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero), 10, 9, 1, TenantAJanRunDetail);
        await InsertRunLogAsync(conn, "Send", "Scheduled", new DateTimeOffset(2026, 2, 15, 8, 0, 0, TimeSpan.Zero), 5, 5, 0, TenantAFebRunDetail);

        // Agrégats de paiement (pipeline.payment_aggregations) — un Calculated, un Suspended (janvier 2026).
        await InsertPaymentAggregationAsync(conn, new DateOnly(2026, 1, 10), 0.2000m, 100.00m, 20.00m, PaymentAggregationStatus.Calculated.ToString(), reason: null);
        await InsertPaymentAggregationAsync(conn, new DateOnly(2026, 1, 15), 0.2000m, 50.00m, 10.00m, PaymentAggregationStatus.Suspended.ToString(), FiscalPendingReasonText);

        // Paramétrage tenant — API01c GET /settings : profil + fiscal + 2 comptes PA + table TVA validée.
        await SeedTenantProfileAsync(conn, TenantACompanyId, TenantASiren, TenantARaisonSociale);
        await SeedFiscalSettingsAsync(conn, TenantACompanyId);

        // Compte PA dont un plug-in EST chargé (Fake) + clé saisie → capacités exposées, HasApiKey=true.
        await SeedPaAccountAsync(conn, TenantACompanyId, FakePluginType, environment: 0, "compte-fake-a", encryptedApiKey: "enc-fake-a", isActive: true);

        // Compte PA dont AUCUN plug-in n'est chargé + aucune clé → PluginAvailable=false, HasApiKey=false.
        await SeedPaAccountAsync(conn, TenantACompanyId, UnregisteredPluginType, environment: 1, "compte-inconnu", encryptedApiKey: null, isActive: false);

        await SeedValidatedTvaMappingAsync(conn, TenantACompanyId);
    }

    private static async Task SeedTenantBAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await SeedIdentityAsync(conn);

        await InsertDocumentAsync(conn, TenantBDocReadyId, "FA-B-001", "invoice", new DateOnly(2026, 1, 12), "ReadyToSend", "Client Delta", 300.00m);

        // Traitement + agrégat distincts (mars 2026) : le tenant B ne voit jamais ceux du tenant A (isolation).
        await InsertRunLogAsync(conn, "Check", "Scheduled", new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero), 2, 2, 0, TenantBRunDetail);
        await InsertPaymentAggregationAsync(conn, new DateOnly(2026, 3, 5), 0.2000m, 300.00m, 60.00m, PaymentAggregationStatus.Calculated.ToString(), reason: null);

        // Profil de paramétrage distinct (API01c) : sert l'isolation /settings (siren ≠ tenant A) ;
        // le tenant B n'a NI compte PA NI table TVA (vue partielle attendue).
        await SeedTenantProfileAsync(conn, TenantBCompanyId, TenantBSiren, TenantBRaisonSociale);
    }

    private static async Task SeedTenantApi04Async(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await SeedIdentityAsync(conn);

        // Table TVA VALIDÉE (une règle) : les tests d'édition API04 partent d'un état validé pour vérifier
        // l'invalidation après mutation, puis la re-validation. Isolée des autres suites (tenant dédié).
        await SeedValidatedTvaMappingAsync(conn, TenantApi04CompanyId);
    }

    /// <summary>
    /// Tenant de verdict/recheck (API02b) : identité (opérateur + lecteur) + profil tenant (SIREN valide,
    /// exigé par SupplierIdentityRule) + table TVA validée dont la part MATCHE la requête du CHECK (Autre) —
    /// pour qu'une re-vérification après verdict B2C atteigne ReadyToSend. Les documents bloqués + pivots
    /// stagés sont seedés FRAIS par test via <see cref="SeedBlockedProfessionalBuyerDocumentAsync"/>.
    /// </summary>
    private static async Task SeedTenantVerdictAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await SeedIdentityAsync(conn);
        await SeedTenantProfileAsync(conn, TenantVerdictCompanyId, VerdictProfileSiren, "Étude Fictïve SVV");
        await SeedValidatedTvaMappingForCheckAsync(conn, TenantVerdictCompanyId);

        // Document ReadyToSend (lecture seule) : sert les 409 « non bloqué » du verdict et du recheck.
        await InsertDocumentAsync(conn, TenantVerdictReadyDocId, "FA-VERDICT-READY", "invoice", new DateOnly(2026, 1, 20), "ReadyToSend", "Client Verdict", 144.00m);
    }

    /// <summary>
    /// Table TVA validée à une règle (REGIME-A → catégorie S, 20 %, part <c>Autre</c>=2) : la part MATCHE la
    /// requête du CHECK générique (<c>TvaMappingPart.Autre</c> — ADR-0004/F03 §2.3 ; le matching de part est
    /// EXACT dans TvaMapper). Indispensable pour qu'une re-vérification atteigne ReadyToSend ; la variante
    /// <see cref="SeedValidatedTvaMappingAsync"/> (part Adjudication=0) ne sert que les lectures /settings.
    /// </summary>
    private static async Task SeedValidatedTvaMappingForCheckAsync(NpgsqlConnection conn, Guid companyId)
    {
        var tableId = Guid.NewGuid();
        await conn.ExecuteAsync(
            """
            INSERT INTO tvamapping.mapping_tables
                (id, company_id, mapping_version, validated_by, validated_date, default_behavior)
            VALUES (@Id, @CompanyId, @Version, @ValidatedBy, @ValidatedDate, 0)
            """,
            new
            {
                Id = tableId,
                CompanyId = companyId,
                Version = "verdict-v1",
                ValidatedBy = "Expert-comptable Verdict",
                ValidatedDate = new DateOnly(2026, 5, 1),
            });

        // part=2 (Autre) ; category=1 (VatCategory.S → taux > 0) ; rate_mode=0 (Fixed) ; rate_value=20 ; vatex absent.
        await conn.ExecuteAsync(
            """
            INSERT INTO tvamapping.mapping_rules
                (table_id, ordinal, source_regime_code, label, part, category, vatex, rate_mode, rate_value)
            VALUES (@TableId, 0, 'REGIME-A', 'Taux normal 20 %', 2, 1, NULL, 0, 20)
            """,
            new { TableId = tableId });
    }

    /// <summary>
    /// Seede un document BLOQUÉ FRAIS (identifiant unique) dans le tenant, avec un pivot stagé dont l'acheteur
    /// porte un indice « société » (déclenche le garde-fou <c>BUYER_LOOKS_PROFESSIONAL</c>, VAL05) mais qui
    /// passe TOUTES les autres règles + le mapping (REGIME-A → S 20 %). Sans verdict : une re-vérification le
    /// laisse Blocked sur le garde-fou. Après verdict « confirmer B2C » : la re-vérification atteint ReadyToSend.
    /// L'empreinte du document EST celle du pivot stagé (le recheck relit et re-vérifie le hash). Retourne l'id.
    /// </summary>
    public async Task<Guid> SeedBlockedProfessionalBuyerDocumentAsync(string tenant, CancellationToken cancellationToken = default)
    {
        var documentId = Guid.NewGuid();
        var number = "FA-VERDICT-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var sourceReference = "src/" + number;
        var pivot = BuildProfessionalBuyerPivot(number, sourceReference);
        var json = CanonicalJson.Serialize(pivot);
        var hash = PayloadHasher.ComputeHash(json);

        await using (var conn = new NpgsqlConnection(ConnectionStringFor(tenant)))
        {
            await conn.OpenAsync(cancellationToken);

            // Document Blocked avec l'empreinte RÉELLE du pivot (le recheck relit le staging par cette clé) ;
            // customer_is_company_hint=true (cohérent avec le pivot) ; montants 120/24/144 (decimal).
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO documents.documents (
                    id, source_reference, document_number, document_type, issue_date, supplier_siren,
                    customer_name, customer_is_company_hint, total_net, total_tax, total_gross, state,
                    payload_hash, first_seen_utc, last_update_utc)
                VALUES (
                    @Id, @SourceRef, @Number, 'invoice', @IssueDate, NULL,
                    @CustomerName, true, 120.00, 24.00, 144.00, 'Blocked',
                    @PayloadHash, @Now, @Now)
                """,
                new
                {
                    Id = documentId,
                    SourceRef = sourceReference,
                    Number = number,
                    IssueDate = new DateOnly(2026, 1, 15),
                    CustomerName = "Acheteur Indice Société",
                    PayloadHash = hash,
                    Now = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero),
                },
                cancellationToken: cancellationToken));

            await InsertEventAsync(
                conn,
                documentId,
                new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero),
                "DocumentBlocked",
                "Garde-fou B2B/B2C : acheteur potentiellement professionnel — verdict opérateur requis (F08 §A.4).",
                payloadSnapshot: null);
        }

        // Stage le pivot canonique (chiffré au repos par le magasin) sous la clé tenant-scopée du document.
        await using var scope = _app!.Services.CreateAsyncScope();
        var staging = scope.ServiceProvider.GetRequiredService<IPayloadStagingStore>();
        await staging.WriteAsync(new StagedPayloadKey(tenant, documentId, hash), json, cancellationToken);

        return documentId;
    }

    /// <summary>
    /// Pose le verdict « confirmer particulier (B2C) » sur un document bloqué, IN-PROCESS dans un scope tenant
    /// (le cycle de vie est tenant-scopé). Permet de préparer un document qui se DÉBLOQUERA à la re-vérification
    /// (le garde-fou B2B/B2C ne bloque plus) — sans passer par l'endpoint HTTP. Attribué à l'opérateur de test.
    /// </summary>
    public async Task ConfirmBuyerB2cInScopeAsync(string tenant, Guid documentId, CancellationToken cancellationToken = default)
    {
        var scopeFactory = _app!.Services.GetRequiredService<ITenantScopeFactory>();
        await using ITenantScope scope = scopeFactory.Create(tenant);
        var lifecycle = scope.Services.GetRequiredService<IDocumentLifecycle>();
        await lifecycle.ConfirmBuyerAsIndividualAsync(documentId, OperatorUserId.ToString(), cancellationToken);
    }

    /// <summary>
    /// Re-vérifie EN MASSE (FIX207) les documents donnés IN-PROCESS dans un scope tenant (le cœur de re-vérification
    /// est tenant-scopé : le tenant est résolu par le scope, comme la requête HTTP). Renvoie la synthèse compteurs.
    /// Le geste est attribué à l'opérateur de test (piste d'audit FIX02 par document, écrite par le cycle de vie).
    /// </summary>
    public async Task<DocumentBulkRecheckSummary> RecheckManyInScopeAsync(
        string tenant, IReadOnlyList<Guid> documentIds, CancellationToken cancellationToken = default)
    {
        var scopeFactory = _app!.Services.GetRequiredService<ITenantScopeFactory>();
        await using ITenantScope scope = scopeFactory.Create(tenant);
        var recheck = scope.Services.GetRequiredService<IDocumentRecheckService>();
        return await recheck.RecheckManyAsync(documentIds, OperatorUserId.ToString(), cancellationToken);
    }

    /// <summary>
    /// Seede un document BLOQUÉ FRAIS dont le pivot N'EST PAS stagé (empreinte sans blob correspondant) : la
    /// re-vérification ne peut pas relire le contenu → issue « contenu indisponible » (409). Couvre le chemin
    /// dégradé « bloquer plutôt qu'envoyer faux » (CLAUDE.md n°3) — une régression qui le mapperait en 200/500
    /// resterait sinon invisible. Retourne l'identifiant.
    /// </summary>
    public async Task<Guid> SeedBlockedDocumentWithoutStagedPivotAsync(string tenant, CancellationToken cancellationToken = default)
    {
        var documentId = Guid.NewGuid();
        var number = "FA-NOSTAGE-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        await using var conn = new NpgsqlConnection(ConnectionStringFor(tenant));
        await conn.OpenAsync(cancellationToken);

        // InsertDocumentAsync pose payload_hash = "hash-<number>" : AUCUN blob n'est stagé pour cette empreinte,
        // donc la relecture du staging lève StagedPayloadNotFoundException → DocumentRecheckResult.ContentUnavailable.
        await InsertDocumentAsync(conn, documentId, number, "invoice", new DateOnly(2026, 1, 16), "Blocked", "Client Sans Pivot", 144.00m);
        return documentId;
    }

    /// <summary>Marqueur de verdict B2C persisté sur le document (assertion du verdict « confirmer particulier »).</summary>
    public async Task<bool> IsBuyerConfirmedAsync(string tenant, Guid documentId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(ConnectionStringFor(tenant));
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT buyer_confirmed_as_individual FROM documents.documents WHERE id = @Id",
            new { Id = documentId },
            cancellationToken: ct));
    }

    /// <summary>Construit un pivot dont l'acheteur déclenche le garde-fou (indice société) mais qui passe le reste du CHECK (REGIME-A → S 20 %).</summary>
    private static PivotDocumentDto BuildProfessionalBuyerPivot(string number, string sourceReference)
    {
        var line = new PivotLineDto(
            description: "Adjudication lot — vase décoratif",
            netAmount: 120.00m,
            quantity: 1m,
            unitPriceNet: 120.00m,
            sourceRegimeCodes: VerdictPivotRegimeCodes,
            taxes: new[] { new PivotLineTaxDto(24.00m, 20m) });

        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: number,
            issueDate: new DateTime(2026, 1, 15),
            sourceReference: sourceReference,
            supplier: new PivotPartyDto("Étude Fictïve SVV"),
            totals: new PivotTotalsDto(120.00m, 24.00m, 144.00m, 144.00m),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: new PivotPartyDto("Acheteur Indice Société", isCompanyHint: true),
            lines: new[] { line });
    }

    /// <summary>
    /// Seede une PROPOSITION de réconciliation FRAÎCHE dans le tenant : archive un document émis (paquet
    /// WORM réel, pour que la confirmation puisse y ajouter un addendum), dépose un PDF dans le pool, et
    /// ajoute une entrée de file « proposition en attente » pointant ce document. Chaque appel crée des
    /// entrées DISTINCTES (pas d'interférence entre tests). Retourne l'entrée, le document et le PDF déposé.
    /// </summary>
    public async Task<(Guid EntryId, Guid DocumentId, string FileName, byte[] Bytes)> SeedReconciliationProposalAsync(
        string tenant,
        CancellationToken cancellationToken = default)
    {
        var docNumber = "FA-RECON-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var documentId = await ArchiveSampleDocumentAsync(tenant, docNumber, new DateOnly(2026, 4, 1), cancellationToken);
        var (entryId, fileName, bytes) = await AddPooledQueueEntryAsync(tenant, documentId, isProposal: true, cancellationToken);
        return (entryId, documentId, fileName, bytes);
    }

    /// <summary>
    /// Seede un ORPHELIN de réconciliation FRAIS dans le tenant : dépose un PDF dans le pool et ajoute une
    /// entrée de file « orphelin ». Retourne l'entrée et le PDF déposé.
    /// </summary>
    public async Task<(Guid EntryId, string FileName, byte[] Bytes)> SeedReconciliationOrphanAsync(
        string tenant,
        CancellationToken cancellationToken = default)
    {
        return await AddPooledQueueEntryAsync(tenant, documentId: null, isProposal: false, cancellationToken);
    }

    private static async Task InsertRunLogAsync(
        NpgsqlConnection conn,
        string runType,
        string runTrigger,
        DateTimeOffset startedAt,
        int processed,
        int succeeded,
        int failed,
        string detail)
    {
        await conn.ExecuteAsync(
            """
            INSERT INTO pipeline.run_logs (
                run_type, run_trigger, started_at, completed_at,
                documents_processed, documents_succeeded, documents_failed, detail)
            VALUES (
                @RunType, @RunTrigger, @StartedAt, @CompletedAt,
                @Processed, @Succeeded, @Failed, @Detail)
            """,
            new
            {
                RunType = runType,
                RunTrigger = runTrigger,
                StartedAt = startedAt,
                CompletedAt = startedAt.AddMinutes(3),
                Processed = processed,
                Succeeded = succeeded,
                Failed = failed,
                Detail = detail,
            });
    }

    private static async Task InsertPaymentAggregationAsync(
        NpgsqlConnection conn,
        DateOnly aggregateDate,
        decimal vatRate,
        decimal taxableBase,
        decimal vatAmount,
        string status,
        string? reason)
    {
        await conn.ExecuteAsync(
            """
            INSERT INTO pipeline.payment_aggregations (
                aggregate_date, vat_rate, taxable_base, vat_amount, status, reason)
            VALUES (
                @AggregateDate, @VatRate, @TaxableBase, @VatAmount, @Status, @Reason)
            """,
            new
            {
                AggregateDate = aggregateDate,
                VatRate = vatRate,
                TaxableBase = taxableBase,
                VatAmount = vatAmount,
                Status = status,
                Reason = reason,
            });
    }

    private static async Task SeedTenantProfileAsync(
        NpgsqlConnection conn,
        Guid companyId,
        string siren,
        string raisonSociale)
    {
        await conn.ExecuteAsync(
            """
            INSERT INTO tenantsettings.tenant_profiles
                (company_id, siren, raison_sociale, address_street, address_postal_code, address_city, address_country, statut)
            VALUES (@CompanyId, @Siren, @RaisonSociale, '1 rue de Test', '35000', 'Rennes', 'FR', 0)
            """,
            new { CompanyId = companyId, Siren = siren, RaisonSociale = raisonSociale });
    }

    private static async Task SeedFiscalSettingsAsync(NpgsqlConnection conn, Guid companyId)
    {
        // Paramètres fiscaux partiels : vat_on_debits renseigné, le reste null (décision en attente) —
        // reportingFrequency en chaîne opaque (jamais interprétée, INV-TENANTSETTINGS-008).
        await conn.ExecuteAsync(
            """
            INSERT INTO tenantsettings.fiscal_settings
                (company_id, vat_on_debits, operation_category, reporting_frequency, fee_imputation_method)
            VALUES (@CompanyId, true, NULL, 'mensuel', NULL)
            """,
            new { CompanyId = companyId });
    }

    private static async Task SeedPaAccountAsync(
        NpgsqlConnection conn,
        Guid companyId,
        string pluginType,
        int environment,
        string accountIdentifiers,
        string? encryptedApiKey,
        bool isActive)
    {
        // encrypted_api_key NON NULL = une clé a été saisie (HasApiKey=true) ; la valeur seedée est un
        // placeholder opaque (la lecture ne sélectionne jamais cette colonne — INV-TENANTSETTINGS-003).
        await conn.ExecuteAsync(
            """
            INSERT INTO tenantsettings.pa_accounts
                (company_id, plugin_type, environment, account_identifiers, encrypted_api_key, is_active)
            VALUES (@CompanyId, @PluginType, @Environment, @AccountIdentifiers, @EncryptedApiKey, @IsActive)
            """,
            new
            {
                CompanyId = companyId,
                PluginType = pluginType,
                Environment = environment,
                AccountIdentifiers = accountIdentifiers,
                EncryptedApiKey = encryptedApiKey,
                IsActive = isActive,
            });
    }

    private static async Task SeedValidatedTvaMappingAsync(NpgsqlConnection conn, Guid companyId)
    {
        // Table TVA VALIDÉE à une règle (catégorie S, taux 20 %, sans VATEX — structurellement valide
        // pour la re-validation au chargement). default_behavior=0 (Block) ; part=0 (Adjudication) ;
        // category=1 (VatCategory.S) ; rate_mode=0 (Fixed).
        var tableId = Guid.NewGuid();
        await conn.ExecuteAsync(
            """
            INSERT INTO tvamapping.mapping_tables
                (id, company_id, mapping_version, validated_by, validated_date, default_behavior)
            VALUES (@Id, @CompanyId, @Version, @ValidatedBy, @ValidatedDate, 0)
            """,
            new
            {
                Id = tableId,
                CompanyId = companyId,
                Version = TenantATvaVersion,
                ValidatedBy = TenantATvaValidatedBy,
                ValidatedDate = new DateOnly(2026, 5, 1),
            });

        await conn.ExecuteAsync(
            """
            INSERT INTO tvamapping.mapping_rules
                (table_id, ordinal, source_regime_code, label, part, category, vatex, rate_mode, rate_value)
            VALUES (@TableId, 0, 'REGIME-A', 'Taux normal 20 %', 0, 1, NULL, 0, 20)
            """,
            new { TableId = tableId });
    }

    private static async Task InsertDocumentAsync(
        NpgsqlConnection conn,
        Guid id,
        string number,
        string type,
        DateOnly issueDate,
        string state,
        string customerName,
        decimal totalGross)
    {
        await conn.ExecuteAsync(
            """
            INSERT INTO documents.documents (
                id, source_reference, document_number, document_type, issue_date, supplier_siren,
                customer_name, customer_is_company_hint, total_net, total_tax, total_gross, state,
                payload_hash, first_seen_utc, last_update_utc)
            VALUES (
                @Id, @SourceRef, @Number, @Type, @IssueDate, NULL,
                @CustomerName, true, @TotalNet, @TotalTax, @TotalGross, @State,
                @PayloadHash, @Now, @Now)
            """,
            new
            {
                Id = id,
                SourceRef = "src/" + number,
                Number = number,
                Type = type,
                IssueDate = issueDate,
                CustomerName = customerName,
                TotalNet = totalGross,
                TotalTax = 0m,
                TotalGross = totalGross,
                State = state,
                PayloadHash = "hash-" + number,
                Now = new DateTimeOffset(issueDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            });
    }

    private static async Task InsertEventAsync(
        NpgsqlConnection conn,
        Guid documentId,
        DateTimeOffset timestampUtc,
        string eventType,
        string? detail,
        string? payloadSnapshot)
    {
        await conn.ExecuteAsync(
            """
            INSERT INTO documents.document_events (
                id, document_id, timestamp_utc, event_type, detail, payload_snapshot)
            VALUES (
                gen_random_uuid(), @DocId, @Now, @EventType, @Detail, @Payload::jsonb)
            """,
            new
            {
                DocId = documentId,
                Now = timestampUtc,
                EventType = eventType,
                Detail = detail,
                Payload = payloadSnapshot,
            });
    }

    /// <summary>
    /// Copie (au mieux) le manifeste de static web assets du projet de test vers le nom attendu par
    /// l'hôte d'entrée, comme le harness E2E du dépôt. Best-effort : les tests d'API n'ont pas besoin des
    /// assets navigateur, donc l'absence du manifeste n'est pas bloquante.
    /// </summary>
    private static void EnsureStaticWebAssetsManifest()
    {
        var source = Path.Combine(
            AppContext.BaseDirectory,
            "Liakont.Console.Api.Tests.Integration.staticwebassets.runtime.json");
        var entryName = Assembly.GetEntryAssembly()?.GetName().Name ?? "testhost";
        var target = Path.Combine(AppContext.BaseDirectory, $"{entryName}.staticwebassets.runtime.json");

        if (File.Exists(source) && !File.Exists(target))
        {
            File.Copy(source, target);
        }
    }

    private static string WithDatabase(string baseConnectionString, string databaseName)
    {
        return new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName,
        }.ToString();
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task<(Guid EntryId, string FileName, byte[] Bytes)> AddPooledQueueEntryAsync(
        string tenant,
        Guid? documentId,
        bool isProposal,
        CancellationToken cancellationToken)
    {
        var fileName = "recon-" + Guid.NewGuid().ToString("N")[..8] + ".pdf";
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.4 reconciliation test " + fileName);

        var scopeFactory = _app!.Services.GetRequiredService<ITenantScopeFactory>();
        await using ITenantScope scope = scopeFactory.Create(tenant);
        var pdfStore = scope.Services.GetRequiredService<IIngestedPdfStore>();
        var queue = scope.Services.GetRequiredService<IReconciliationQueueStore>();

        await using (var ms = new MemoryStream(bytes))
        {
            await pdfStore.SavePooledPdfAsync(tenant, fileName, ms, cancellationToken);
        }

        var pooled = await pdfStore.ListPooledPdfsAsync(tenant, cancellationToken);
        var reference = pooled.Single(p => string.Equals(p.FileName, fileName, StringComparison.Ordinal));

        var nowUtc = DateTimeOffset.UtcNow;
        var entry = isProposal
            ? ReconciliationQueueEntry.PendingProposal(
                reference.PoolPdfId,
                reference.FileName,
                documentId!.Value,
                "Proposition (date + montant) — fixture API04.",
                nowUtc)
            : ReconciliationQueueEntry.Orphan(
                reference.PoolPdfId,
                reference.FileName,
                "Orphelin (aucune correspondance) — fixture API04.",
                nowUtc);

        await queue.AddAsync(entry, cancellationToken);
        return (entry.Id, reference.FileName, bytes);
    }

    private string ConnectionStringFor(string tenant) =>
        _connectionsByTenant.TryGetValue(tenant, out string? conn)
            ? conn
            : throw new InvalidOperationException($"Tenant inconnu du harness : {tenant}.");

    private void DeleteArchiveStore()
    {
        if (string.IsNullOrEmpty(_archiveStoreRoot) || !Directory.Exists(_archiveStoreRoot))
        {
            return;
        }

        // Les paquets sont en lecture seule (WORM applicatif) : lever l'attribut avant suppression.
        foreach (string file in Directory.EnumerateFiles(_archiveStoreRoot, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_archiveStoreRoot, recursive: true);
    }

    private void DeleteIngestionPdfStore()
    {
        if (string.IsNullOrEmpty(_ingestionPdfRoot) || !Directory.Exists(_ingestionPdfRoot))
        {
            return;
        }

        Directory.Delete(_ingestionPdfRoot, recursive: true);
    }

    private void DeleteStagingStore()
    {
        if (string.IsNullOrEmpty(_stagingRoot) || !Directory.Exists(_stagingRoot))
        {
            return;
        }

        Directory.Delete(_stagingRoot, recursive: true);
    }
}
