namespace Liakont.Host.Startup;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Stratum.Common.Infrastructure.Keycloak;

/// <summary>Classification de l'état du realm de dev (sortie de <see cref="DevRealmHealthCheck.Classify"/>).</summary>
internal enum DevRealmOutcome
{
    /// <summary>Realm joignable et compte de dev attendu présent.</summary>
    Healthy,

    /// <summary>Realm joignable mais compte attendu absent — import probablement sauté (volume résiduel).</summary>
    Stale,

    /// <summary>Sonde non concluante (admin non configuré, Keycloak injoignable au démarrage) — best-effort.</summary>
    Indeterminate,
}

/// <summary>
/// Garde-fou d'EXPÉRIENCE DE DÉVELOPPEMENT (FIX07a) : au démarrage en Development, détecte le cas
/// « realm Keycloak périmé ». Un volume Keycloak résiduel d'un essai antérieur fait sauter l'import
/// (<c>--import-realm</c>, stratégie IGNORE_EXISTING) : le realm <c>liakont-dev</c> garde alors ses
/// anciens comptes (usernames e-mail) et la console répond « identifiants invalides » SANS aucun
/// signal serveur. Ce check best-effort, Development uniquement, émet dans ce cas un WARN explicite
/// pointant vers le remède (<c>tools/keycloak-dev.ps1 reset</c>).
/// <para>
/// Garde-fous : ne tourne QUE si l'environnement est Development. Best-effort total — il ne bloque
/// JAMAIS le démarrage (toute erreur ou indisponibilité transitoire = silencieux), n'écrit dans
/// aucune base et ne lit que l'API d'administration Keycloak de dev (admin configuré dans la
/// section <c>Keycloak</c> d'appsettings.Development.json). Aucun secret n'est journalisé.
/// </para>
/// </summary>
internal static partial class DevRealmHealthCheck
{
    // Compte de dev distinctif attendu (realm-export.json) : username COURT, absent d'un realm
    // périmé (dont les usernames étaient des e-mails). Sa présence prouve un import à jour.
    private const string ExpectedDevUsername = "parametrage";

    /// <summary>Émet un avertissement si le realm Keycloak de dev est joignable mais périmé.</summary>
    public static async Task WarnIfDevRealmStaleAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Liakont.Host.Startup.DevRealmHealthCheck");

        var admin = app.Configuration.GetSection(KeycloakAdminOptions.SectionName).Get<KeycloakAdminOptions>();
        var realmName = app.Configuration["DevTenantSeed:RealmName"];
        if (string.IsNullOrWhiteSpace(realmName))
        {
            realmName = admin?.PrimaryRealmName;
        }

        if (admin is null || !admin.IsConfigured || string.IsNullOrWhiteSpace(realmName))
        {
            // Sans admin Keycloak configuré, on ne peut pas inspecter les comptes : on s'abstient.
            LogIndeterminate(logger, realmName ?? "?");
            return;
        }

        var reachable = false;
        var accountPresent = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var token = await AcquireAdminTokenAsync(http, admin, cts.Token);
            accountPresent = await IsExpectedAccountPresentAsync(http, admin.AdminBaseUrl, realmName, token, cts.Token);
            reachable = true;
        }
        catch (Exception)
        {
            // Best-effort : Keycloak peut être en cours de démarrage. Une indisponibilité transitoire
            // n'est pas un realm « périmé » — on reste silencieux (debug) et on ne bloque pas le boot.
            reachable = false;
        }

        switch (Classify(adminConfigured: true, realmReachable: reachable, accountPresent: accountPresent))
        {
            case DevRealmOutcome.Healthy:
                LogHealthy(logger, realmName, ExpectedDevUsername);
                break;
            case DevRealmOutcome.Stale:
                LogStale(logger, realmName, ExpectedDevUsername);
                break;
            default:
                LogIndeterminate(logger, realmName);
                break;
        }
    }

    /// <summary>
    /// Décision PURE (testable) : à partir des signaux de sonde, classe l'état du realm de dev.
    /// « Joignable + compte attendu absent » est la signature de l'import sauté (volume résiduel).
    /// </summary>
    internal static DevRealmOutcome Classify(bool adminConfigured, bool realmReachable, bool accountPresent)
    {
        if (!adminConfigured || !realmReachable)
        {
            return DevRealmOutcome.Indeterminate;
        }

        return accountPresent ? DevRealmOutcome.Healthy : DevRealmOutcome.Stale;
    }

    /// <summary>Jeton admin du realm master (grant password, client admin-cli) — dev local.</summary>
    private static async Task<string> AcquireAdminTokenAsync(HttpClient http, KeycloakAdminOptions admin, CancellationToken ct)
    {
        var url = $"{admin.AdminBaseUrl.TrimEnd('/')}/realms/master/protocol/openid-connect/token";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = admin.AdminUsername,
            ["password"] = admin.AdminPassword,
        });

        using var response = await http.PostAsync(url, content, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Réponse de jeton Keycloak sans access_token.");
    }

    /// <summary>Vrai si le compte de dev attendu existe dans le realm (sonde exacte par username).</summary>
    private static async Task<bool> IsExpectedAccountPresentAsync(
        HttpClient http, string adminBaseUrl, string realmName, string token, CancellationToken ct)
    {
        var url = $"{adminBaseUrl.TrimEnd('/')}/admin/realms/{Uri.EscapeDataString(realmName)}"
            + $"/users?username={ExpectedDevUsername}&exact=true";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request, ct);

        // Realm absent (404) = compte attendu absent → classé « périmé », pas indéterminé.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0;
    }

    [LoggerMessage(
        EventId = 7201,
        Level = LogLevel.Warning,
        Message = "Realm Keycloak de dev « {Realm} » joignable mais le compte attendu « {Account} » est ABSENT : "
            + "l'import du realm a probablement été sauté (volume Keycloak résiduel). Réinitialisez avec : "
            + "powershell -ExecutionPolicy Bypass -File tools/keycloak-dev.ps1 reset")]
    private static partial void LogStale(ILogger logger, string realm, string account);

    [LoggerMessage(EventId = 7202, Level = LogLevel.Debug, Message = "Realm Keycloak de dev « {Realm} » conforme (compte « {Account} » présent).")]
    private static partial void LogHealthy(ILogger logger, string realm, string account);

    [LoggerMessage(
        EventId = 7203,
        Level = LogLevel.Debug,
        Message = "Vérification du realm Keycloak de dev « {Realm} » ignorée (admin non configuré ou Keycloak injoignable au démarrage) — best-effort.")]
    private static partial void LogIndeterminate(ILogger logger, string realm);
}
