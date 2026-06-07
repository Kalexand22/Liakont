namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using Dapper;
using Liakont.Host.Startup;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
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
///         production (<c>PermissionAuthorizationHandler</c>, décision en base) ;</item>
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

    // Utilisateurs seedés (claim NameIdentifier porté par X-Test-User).
    public static readonly Guid ReaderUserId = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid NoPermissionUserId = new("22222222-2222-2222-2222-222222222222");

    // Documents seedés dans le TENANT A.
    public static readonly Guid TenantADocReadyId = new("0a000001-0000-0000-0000-000000000001");
    public static readonly Guid TenantADocBlockedId = new("0a000002-0000-0000-0000-000000000002");
    public static readonly Guid TenantADocIssuedId = new("0a000003-0000-0000-0000-000000000003");

    // Document seedé dans le TENANT B (sert l'isolation : il n'existe pas dans A).
    public static readonly Guid TenantBDocReadyId = new("0b000001-0000-0000-0000-000000000001");

    private static readonly Guid ConsoleReaderRoleId = new("33333333-3333-3333-3333-333333333333");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly int _port = GetFreePort();
    private WebApplication? _app;

    public string BaseUrl => $"http://127.0.0.1:{_port}";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var systemConn = _postgres.GetConnectionString();
        var tenantAConn = WithDatabase(systemConn, "tc_tenant_a");
        var tenantBConn = WithDatabase(systemConn, "tc_tenant_b");

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

        // Keycloak factice (login désactivé) : l'abstraction d'IdP exige une autorité configurée, mais
        // aucun chemin OIDC/JWT n'est exercé — le schéma « Test » est le schéma par défaut ci-dessous.
        builder.Configuration["Keycloak:Authority"] = "http://127.0.0.1:1/realms/test";
        builder.Configuration["Keycloak:ClientId"] = "test";
        builder.Configuration["Keycloak:RequireHttpsMetadata"] = "false";
        builder.Configuration["Keycloak:UseKeycloak"] = "false";

        AppBootstrap.ConfigureServices(builder);

        // Remplace l'authentification par le schéma de test (X-Test-User → NameIdentifier), schéma par
        // défaut. L'autorisation reste celle de production (décision en base par tenant).
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

        await SeedTenantAAsync(tenantAConn);
        await SeedTenantBAsync(tenantBConn);

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
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await DisposeAsync();
    }

    /// <summary>
    /// Crée un client HTTP ciblant l'hôte in-process, avec l'en-tête de tenant (<c>X-Tenant-Id</c>) et,
    /// si fourni, l'identité de test (<c>X-Test-User</c>). Sans utilisateur, la requête est anonyme.
    /// </summary>
    public HttpClient CreateClient(string tenantId, Guid? userId = null)
    {
        var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
        if (userId is { } user)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user.ToString());
        }

        return client;
    }

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

    private static async Task SeedIdentityAsync(NpgsqlConnection conn)
    {
        // Rôle « console-reader » porteur de liakont.read + un utilisateur qui le détient (200) et un
        // utilisateur sans aucun rôle (403). L'autorisation de production lit ces lignes par tenant.
        await conn.ExecuteAsync(
            """
            INSERT INTO identity.roles (id, name, description, is_system)
            VALUES (@Id, 'console-reader', 'Lecture console (tests)', false)
            ON CONFLICT (id) DO NOTHING
            """,
            new { Id = ConsoleReaderRoleId });

        await conn.ExecuteAsync(
            """
            INSERT INTO identity.grants (role_id, permission, module_source)
            VALUES (@RoleId, 'liakont.read', 'liakont')
            ON CONFLICT (role_id, permission) DO NOTHING
            """,
            new { RoleId = ConsoleReaderRoleId });

        await conn.ExecuteAsync(
            """
            INSERT INTO identity.users (id, username, email, display_name, password_hash, is_active)
            VALUES
                (@ReaderId, 'console.reader', 'reader@test.local', 'Console Reader', 'x', true),
                (@NoPermId, 'console.noperm', 'noperm@test.local', 'Console NoPerm', 'x', true)
            ON CONFLICT (id) DO NOTHING
            """,
            new { ReaderId = ReaderUserId, NoPermId = NoPermissionUserId });

        await conn.ExecuteAsync(
            """
            INSERT INTO identity.user_roles (user_id, role_id)
            VALUES (@ReaderId, @RoleId)
            ON CONFLICT (user_id, role_id) DO NOTHING
            """,
            new { ReaderId = ReaderUserId, RoleId = ConsoleReaderRoleId });
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
        await InsertPaymentAggregationAsync(conn, new DateOnly(2026, 1, 10), 0.2000m, 100.00m, 20.00m, "Calculated", reason: null);
        await InsertPaymentAggregationAsync(conn, new DateOnly(2026, 1, 15), 0.2000m, 50.00m, 10.00m, "Suspended", FiscalPendingReasonText);
    }

    private static async Task SeedTenantBAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await SeedIdentityAsync(conn);

        await InsertDocumentAsync(conn, TenantBDocReadyId, "FA-B-001", "invoice", new DateOnly(2026, 1, 12), "ReadyToSend", "Client Delta", 300.00m);

        // Traitement + agrégat distincts (mars 2026) : le tenant B ne voit jamais ceux du tenant A (isolation).
        await InsertRunLogAsync(conn, "Check", "Scheduled", new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero), 2, 2, 0, TenantBRunDetail);
        await InsertPaymentAggregationAsync(conn, new DateOnly(2026, 3, 5), 0.2000m, 300.00m, 60.00m, "Calculated", reason: null);
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
}
