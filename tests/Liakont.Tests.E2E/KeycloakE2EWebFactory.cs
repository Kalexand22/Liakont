namespace Liakont.Tests.E2E;

using System.Net;
using System.Net.Sockets;
using System.Reflection;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Liakont.Host.Startup;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
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

        _keycloak = new ContainerBuilder()
            .WithImage("quay.io/keycloak/keycloak:26.0")
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
}
