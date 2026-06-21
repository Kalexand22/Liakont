namespace Liakont.Tests.E2E;

using System.Net;
using System.Net.Sockets;
using System.Reflection;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Liakont.Host.Startup;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Stratum.Common.Abstractions.MultiTenancy;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Démarre la plateforme Liakont (<see cref="Liakont.Host"/>) adossée à des conteneurs
/// PostgreSQL + Keycloak (Testcontainers). Configure l'authentification OIDC de sorte que
/// les tests E2E exercent le flux de login Keycloak complet (realm <c>liakont-dev</c>).
/// </summary>
/// <remarks>
/// Adapté de <c>Stratum.Tests.E2E.KeycloakE2EWebFactory</c> (harness du socle, hors périmètre
/// du vendoring SOL01 qui ne copie que <c>src/</c>) — voir
/// <c>docs/architecture/provenance-socle-stratum.md</c>. Le spécifique ERP (BugCapture, mock
/// GitHub) a été retiré ; la configuration cible les clés réelles du Host Liakont.
///
/// Tenant : en développement comme en E2E, le tenant <c>default</c> (résolu depuis le realm
/// <c>liakont-dev</c> via <c>Keycloak:RealmTenantMap</c>) partage la base du système — d'où
/// <c>TenantConnections:ConnectionStrings:default</c> pointé sur la même connexion que
/// <c>Database:ConnectionString</c>. L'utilisateur provisionné au login OIDC est donc lisible
/// sans provisioning d'une base de tenant séparée (cohérent avec appsettings.Development.json).
/// </remarks>
public sealed class KeycloakE2EWebFactory : IAsyncLifetime, IAsyncDisposable
{
    private const string RealmName = "liakont-dev";

    // company_id des utilisateurs du realm E2E (keycloak-e2e-realm.json) : default partage -001 avec
    // lecture/operateur/parametrage/superviseur ; tenant2 porte -002 (RLM01). Ces valeurs doivent
    // COÏNCIDER avec l'attribut company_id du realm fixture et le backfill V017 (cohérence des sources).
    private const string DefaultTenantId = "default";
    private const string DefaultCompanyId = "00000000-0000-4000-a000-000000000001";
    private const string Tenant2Id = "tenant2";
    private const string Tenant2CompanyId = "00000000-0000-4000-a000-000000000002";
    private const string Tenant2Database = "liakont_e2e_tenant2";
    private const string Tenant2RealmName = "liakont-dev-recette2";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly int _port = GetFreePort();
    private IContainer? _keycloak;
    private WebApplication? _app;

    public string BaseUrl => $"http://127.0.0.1:{_port}";

    /// <summary>URL d'autorité OIDC Keycloak du realm de test E2E.</summary>
    public string KeycloakAuthority { get; private set; } = string.Empty;

    /// <summary>
    /// Chaîne de connexion PostgreSQL du conteneur de test. En E2E le tenant <c>default</c> partage cette
    /// base avec le système (cf. remarque de classe) : un test peut donc y SEEDER des données de tenant
    /// (ex. un document) directement, avant de naviguer dans la console. Disponible une fois la factory
    /// initialisée (fixture de collection, avant tout test).
    /// </summary>
    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        // Démarrage de PostgreSQL et Keycloak en parallèle.
        var pgTask = _postgres.StartAsync();

        var realmPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "keycloak-e2e-realm.json");
        var realmJson = await File.ReadAllTextAsync(realmPath);

        // Keycloak n'accepte pas '*' comme joker de port (seulement en suffixe de chemin) :
        // on substitue le port réel de l'application dans les redirect URIs / web origins.
        realmJson = realmJson
            .Replace("127.0.0.1:*", $"127.0.0.1:{_port}", StringComparison.Ordinal)
            .Replace("localhost:*", $"localhost:{_port}", StringComparison.Ordinal);
        var realmBytes = System.Text.Encoding.UTF8.GetBytes(realmJson);

        // Tag de PATCH précis (non flottant) — politique de version Keycloak : ADR-0020 (avenant).
        _keycloak = new ContainerBuilder()
            .WithImage("quay.io/keycloak/keycloak:26.0.8")
            .WithCommand("start-dev", "--import-realm")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
            .WithResourceMapping(realmBytes, "/opt/keycloak/data/import/keycloak-e2e-realm.json")
            .WithPortBinding(8080, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r
                    .ForPath($"/realms/{RealmName}/.well-known/openid-configuration")
                    .ForPort(8080)
                    .ForStatusCode(HttpStatusCode.OK)))
            .Build();

        var kcTask = _keycloak.StartAsync();

        await Task.WhenAll(pgTask, kcTask);

        var kcPort = _keycloak.GetMappedPublicPort(8080);

        // 127.0.0.1 explicite pour éviter l'ambiguïté IPv4/IPv6 de "localhost" sous Windows.
        KeycloakAuthority = $"http://127.0.0.1:{kcPort}/realms/{RealmName}";

        // Vérifie que les métadonnées OIDC Keycloak sont joignables depuis ce processus.
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var metadataUrl = $"{KeycloakAuthority}/.well-known/openid-configuration";
        Console.WriteLine($"[KeycloakE2E] Sonde des métadonnées OIDC : {metadataUrl}");
        var oidcConfig = await probe.GetAsync(metadataUrl);
        Console.WriteLine($"[KeycloakE2E] Statut métadonnées OIDC : {oidcConfig.StatusCode}");
        oidcConfig.EnsureSuccessStatusCode();

        // Construction de l'application Liakont sur le répertoire du Host (src/Host/Liakont.Host).
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

        // Base de données : le système ET le tenant "default" partagent la même base (cf. remarque).
        var connectionString = _postgres.GetConnectionString();
        builder.Configuration["Database:ConnectionString"] = connectionString;
        builder.Configuration["TenantConnections:ConnectionStrings:default"] = connectionString;

        // RLM04 — E2E de clôture : le 2e tenant (tenant2, company_id -002) a sa PROPRE base (db/realm/
        // company_id UNIQUE imposé par V008/V010/V017), créée et migrée après l'init (SeedE2ETenantsAsync).
        // Son override de connexion est posé ICI (avant Build, sinon IOptions est figé) pour que la
        // résolution de tenant ouvre bien sa base lors du login de clôture.
        var tenant2ConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = Tenant2Database,
        }.ToString();
        builder.Configuration[$"TenantConnections:ConnectionStrings:{Tenant2Id}"] = tenant2ConnectionString;

        // Keycloak OIDC — realm liakont-dev, client liakont (clés réelles du Host Liakont).
        builder.Configuration["Keycloak:Authority"] = KeycloakAuthority;
        builder.Configuration["Keycloak:ClientId"] = "liakont";
        builder.Configuration["Keycloak:ClientSecret"] = "liakont-dev-secret";
        builder.Configuration["Keycloak:RequireHttpsMetadata"] = "false";
        builder.Configuration["Keycloak:UseKeycloak"] = "true";
        builder.Configuration[$"Keycloak:RealmTenantMap:{RealmName}"] = "default";

        AppBootstrap.ConfigureServices(builder);

        builder.Services.AddHttpContextAccessor();

        // Pont d'état d'authentification SSR ↔ circuit pour les tests (voir la classe pour le détail).
        builder.Services.AddScoped<AuthenticationStateProvider, E2EAuthenticationStateProvider>();

        _app = builder.Build();

        await AppBootstrap.InitializeDataAsync(_app);

        // RLM04 — En env Test, DevTenantSeeder ne tourne pas (Development-only) : outbox.tenants serait
        // VIDE et le cross-check RLM03 (ADR-0021 §2b) 403erait TOUT utilisateur de tenant (la résolution
        // company_id→tenant échouerait). On seede ici default (-001) et tenant2 (-002) de bout en bout,
        // ce qui DÉBLOQUE le login OIDC d'un utilisateur de tenant dans le realm partagé (E2E de clôture).
        await SeedE2ETenantsAsync(_app, connectionString);

        AppBootstrap.ConfigureMiddleware(_app);

        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        if (_keycloak is not null)
        {
            await _keycloak.DisposeAsync();
        }

        await _postgres.DisposeAsync().AsTask();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await DisposeAsync();
    }

    private static void EnsureStaticWebAssetsManifest()
    {
        var source = Path.Combine(
            AppContext.BaseDirectory, "Liakont.Tests.E2E.staticwebassets.runtime.json");
        var entryName = Assembly.GetEntryAssembly()?.GetName().Name ?? "testhost";
        var target = Path.Combine(
            AppContext.BaseDirectory, $"{entryName}.staticwebassets.runtime.json");

        if (!File.Exists(source))
        {
            throw new FileNotFoundException(
                $"Manifest des static web assets introuvable à « {source} ». " +
                "Le projet de test E2E doit être compilé avant l'exécution des tests.");
        }

        if (!File.Exists(target))
        {
            File.Copy(source, target);
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Seede le registre <c>outbox.tenants</c> (base SYSTÈME) pour l'isolation par claim en E2E :
    /// <list type="bullet">
    ///   <item><c>default</c> (company_id -001) → partage la base système (comme en dev) ; débloque le
    ///         login des utilisateurs lecture/operateur/parametrage/superviseur.</item>
    ///   <item><c>tenant2</c> (company_id -002) → 2e tenant de bout en bout : base dédiée créée puis
    ///         migrée, pour exercer l'isolation réelle (deux company_id distincts, deux bases).</item>
    /// </list>
    /// Idempotent (<c>ON CONFLICT DO NOTHING</c>). Appelé APRÈS la migration du système (la table
    /// <c>outbox.tenants</c> existe) et AVANT le démarrage de l'application.
    /// </summary>
    private static async Task SeedE2ETenantsAsync(WebApplication app, string systemConnectionString)
    {
        var systemDatabase = new NpgsqlConnectionStringBuilder(systemConnectionString).Database!;

        await using (var connection = new NpgsqlConnection(systemConnectionString))
        {
            await connection.OpenAsync();

            // 1. Tenant `default` : sa base EST la base système (cf. remarque de classe).
            await InsertTenantAsync(
                connection,
                DefaultTenantId,
                "Tenant E2E par defaut",
                "dev@liakont.local",
                systemDatabase,
                RealmName,
                DefaultCompanyId);

            // 2. Tenant `tenant2` : base propre (UNIQUE), créée puis migrée par MigrateExistingTenantsAsync.
            await CreateDatabaseIfMissingAsync(connection, Tenant2Database);
            await InsertTenantAsync(
                connection,
                Tenant2Id,
                "Tenant E2E 2 (isolation)",
                "admin@tenant2.local",
                Tenant2Database,
                Tenant2RealmName,
                Tenant2CompanyId);
        }

        // Migre les bases des tenants actifs : `default` (= base système, déjà migrée → no-op idempotent)
        // et `tenant2` (base fraîche → tous les schémas de module). Échoue bruyamment si une migration rate.
        var provisioner = app.Services.GetRequiredService<ITenantProvisioningService>();
        await provisioner.MigrateExistingTenantsAsync();
    }

    private static async Task InsertTenantAsync(
        NpgsqlConnection connection,
        string id,
        string displayName,
        string adminEmail,
        string databaseName,
        string realmName,
        string companyId)
    {
        const string sql = """
            INSERT INTO outbox.tenants (id, display_name, admin_email, database_name, realm_name, client_secret, company_id)
            VALUES (@id, @displayName, @adminEmail, @databaseName, @realmName, @clientSecret, @companyId)
            ON CONFLICT DO NOTHING
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("displayName", displayName);
        command.Parameters.AddWithValue("adminEmail", adminEmail);
        command.Parameters.AddWithValue("databaseName", databaseName);
        command.Parameters.AddWithValue("realmName", realmName);
        command.Parameters.AddWithValue("clientSecret", "e2e-fixture-secret");
        command.Parameters.AddWithValue("companyId", Guid.Parse(companyId));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateDatabaseIfMissingAsync(NpgsqlConnection connection, string databaseName)
    {
        await using (var check = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection))
        {
            check.Parameters.AddWithValue("name", databaseName);
            if (await check.ExecuteScalarAsync() is not null)
            {
                return;
            }
        }

        // CREATE DATABASE n'accepte ni paramètre lié ni transaction : le nom est une constante de test
        // (jamais une entrée externe), échappé par des guillemets doubles.
        var quoted = "\"" + databaseName.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        await using var create = new NpgsqlCommand($"CREATE DATABASE {quoted}", connection);
        await create.ExecuteNonQueryAsync();
    }
}
