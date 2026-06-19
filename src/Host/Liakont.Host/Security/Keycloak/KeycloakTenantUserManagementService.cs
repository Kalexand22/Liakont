namespace Liakont.Host.Security.Keycloak;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Host.Notifications;
using Liakont.Host.Security.Abstractions;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Keycloak;
using Stratum.Modules.Notification.Contracts;

/// <summary>
/// Implémentation Keycloak de <see cref="ITenantUserManagementService"/> (RB4) — couche d'auth du Host,
/// seul endroit autorisé à parler à l'IdP concret (blueprint §6). Réutilise le client HTTP « KeycloakAdmin »
/// et <see cref="KeycloakAdminTokenService"/> (socle, NON modifié) pour le LISTING par <c>company_id</c>,
/// et <see cref="IKeycloakUserProvisioner.ResetPasswordAsync"/> (socle) pour la réinitialisation. La
/// résolution realm + company_id suit la même règle que <see cref="KeycloakTenantUserProvisioner"/>
/// (realm partagé en SaaS, realm du tenant en dédié ; company_id du profil puis du registre).
/// </summary>
internal sealed partial class KeycloakTenantUserManagementService : ITenantUserManagementService
{
    // Rôles realm techniques de Keycloak à ne pas présenter comme rôles métier dans la liste.
    private static readonly HashSet<string> NoiseRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "offline_access", "uma_authorization", "default-roles-liakont", "default-roles-bucodi",
    };

    private readonly ITenantQueries _tenantQueries;
    private readonly ITenantScopeFactory _scopeFactory;
    private readonly IKeycloakUserProvisioner _keycloak;
    private readonly IEmailTransport _emailTransport;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly KeycloakAdminOptions _keycloakOptions;
    private readonly SmtpOptions _smtpOptions;
    private readonly bool _dedicatedRealmPerTenant;
    private readonly ILogger<KeycloakTenantUserManagementService> _logger;

    public KeycloakTenantUserManagementService(
        ITenantQueries tenantQueries,
        ITenantScopeFactory scopeFactory,
        IKeycloakUserProvisioner keycloak,
        IEmailTransport emailTransport,
        IHttpClientFactory httpClientFactory,
        IOptions<KeycloakAdminOptions> keycloakOptions,
        IOptions<SmtpOptions> smtpOptions,
        IConfiguration configuration,
        ILogger<KeycloakTenantUserManagementService> logger)
    {
        _tenantQueries = tenantQueries;
        _scopeFactory = scopeFactory;
        _keycloak = keycloak;
        _emailTransport = emailTransport;
        _httpClientFactory = httpClientFactory;
        _keycloakOptions = keycloakOptions.Value;
        _smtpOptions = smtpOptions.Value;
        _dedicatedRealmPerTenant =
            configuration.GetValue<bool>($"{KeycloakAdminOptions.SectionName}:DedicatedRealmPerTenant");
        _logger = logger;
    }

    public async Task<IReadOnlyList<TenantUserLine>> ListUsersAsync(
        string tenantId, CancellationToken cancellationToken = default)
    {
        var (realm, companyId) = await ResolveContextAsync(tenantId, cancellationToken);

        var client = await CreateAuthenticatedClientAsync(cancellationToken);

        // briefRepresentation=false : nécessaire pour récupérer les ATTRIBUTS (dont company_id), omis de
        // la représentation brève par défaut. q=company_id:<val> filtre côté serveur ; on RE-FILTRE
        // défensivement par company_id pour ne JAMAIS renvoyer un compte d'un autre tenant (règle n°17).
        var listUrl = $"{AdminBase()}/admin/realms/{realm}/users"
            + $"?q=company_id:{Uri.EscapeDataString(companyId)}&briefRepresentation=false&max=500";

        var response = await client.GetAsync(listUrl, cancellationToken);
        await EnsureSuccessAsync(response, "Lister les utilisateurs du tenant", cancellationToken);

        var users = await response.Content.ReadFromJsonAsync<List<KeycloakUserRepresentation>>(cancellationToken) ?? [];

        var lines = new List<TenantUserLine>();
        foreach (var user in users)
        {
            if (user.Id is null || !MatchesCompany(user, companyId))
            {
                continue;
            }

            var roles = await GetRealmRolesAsync(client, realm, user.Id, cancellationToken);
            lines.Add(new TenantUserLine
            {
                IdpUserId = user.Id,
                Username = user.Username ?? string.Empty,
                Email = user.Email ?? string.Empty,
                DisplayName = BuildDisplayName(user),
                Enabled = user.Enabled,
                Roles = roles,
            });
        }

        return lines
            .OrderBy(l => l.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<TenantUserPasswordResetResult> ResetPasswordAsync(
        string tenantId, string idpUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idpUserId))
        {
            return TenantUserPasswordResetResult.Failed("Identifiant d'utilisateur manquant.");
        }

        var (realm, companyId) = await ResolveContextAsync(tenantId, cancellationToken);

        var client = await CreateAuthenticatedClientAsync(cancellationToken);
        var userUrl = $"{AdminBase()}/admin/realms/{realm}/users/{Uri.EscapeDataString(idpUserId)}";

        var getResponse = await client.GetAsync(userUrl, cancellationToken);
        if (!getResponse.IsSuccessStatusCode)
        {
            return TenantUserPasswordResetResult.Failed(
                $"Utilisateur introuvable dans le realm « {realm} ».");
        }

        var user = await getResponse.Content.ReadFromJsonAsync<KeycloakUserRepresentation>(cancellationToken);

        // Garde tenant-scope (règle n°17) : on ne réinitialise QUE le mot de passe d'un compte du tenant.
        if (user is null || !MatchesCompany(user, companyId))
        {
            return TenantUserPasswordResetResult.Failed(
                "Cet utilisateur n'appartient pas au tenant sélectionné.");
        }

        // Mot de passe temporaire aléatoire, changement forcé au prochain login (temporary:true). Il ne
        // quitte cette méthode QUE par l'email d'invitation (synchrone, jamais persisté/loggé) ou, à
        // défaut, par le résultat — une seule fois (règle n°18).
        var temporaryPassword = GenerateTemporaryPassword();
        await _keycloak.ResetPasswordAsync(realm, idpUserId, temporaryPassword, temporary: true, cancellationToken);

        var sent = await TrySendResetEmailAsync(user, temporaryPassword, cancellationToken);
        LogPasswordReset(_logger, idpUserId, tenantId, sent);

        return new TenantUserPasswordResetResult
        {
            Success = true,
            InvitationEmailSent = sent,
            TemporaryPassword = sent ? null : temporaryPassword,
        };
    }

    /// <summary>Mot de passe temporaire aléatoire (base64url) — l'IdP force le changement au login.</summary>
    private static string GenerateTemporaryPassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');

    private static bool MatchesCompany(KeycloakUserRepresentation user, string companyId) =>
        user.Attributes is not null
        && user.Attributes.TryGetValue("company_id", out var values)
        && values.Any(v => string.Equals(v, companyId, StringComparison.OrdinalIgnoreCase));

    private static string BuildDisplayName(KeycloakUserRepresentation user)
    {
        var name = string.Join(' ', new[] { user.FirstName, user.LastName }
            .Where(p => !string.IsNullOrWhiteSpace(p)));
        return string.IsNullOrWhiteSpace(name) ? user.Username ?? string.Empty : name;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Keycloak — {operation} a échoué ({response.StatusCode}) : {body}");
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Mot de passe réinitialisé pour l'utilisateur '{UserId}' (tenant '{TenantId}', email envoyé : {EmailSent}).")]
    private static partial void LogPasswordReset(ILogger logger, string userId, string tenantId, bool emailSent);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Envoi de l'email de réinitialisation impossible pour '{Username}' — le mot de passe temporaire est remis à l'opérateur.")]
    private static partial void LogResetEmailFailed(ILogger logger, string username, Exception exception);

    private async Task<IReadOnlyList<string>> GetRealmRolesAsync(
        HttpClient client, string realm, string userId, CancellationToken ct)
    {
        var url = $"{AdminBase()}/admin/realms/{realm}/users/{Uri.EscapeDataString(userId)}/role-mappings/realm";
        var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var roles = await response.Content.ReadFromJsonAsync<List<KeycloakRoleRepresentation>>(ct) ?? [];
        return roles
            .Select(r => r.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n) && !NoiseRoles.Contains(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    /// <summary>
    /// Realm cible + company_id du tenant — même règle que <see cref="KeycloakTenantUserProvisioner"/>
    /// (réutilisée volontairement : une seule source de vérité du couplage tenant↔realm↔société).
    /// </summary>
    private async Task<(string Realm, string CompanyId)> ResolveContextAsync(string tenantId, CancellationToken ct)
    {
        if (!_keycloakOptions.IsConfigured)
        {
            throw new InvalidOperationException(
                "Le fournisseur d'identité n'est pas configuré sur cette instance (section Keycloak).");
        }

        var tenant = await _tenantQueries.GetByIdAsync(tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant « {tenantId} » introuvable.");

        var realm = _dedicatedRealmPerTenant ? tenant.RealmName : _keycloakOptions.PrimaryRealmName;
        if (string.IsNullOrWhiteSpace(realm))
        {
            throw new InvalidOperationException(
                "Realm d'identité non configuré pour ce tenant (Keycloak:PrimaryRealmName en SaaS partagé).");
        }

        await using var scope = _scopeFactory.Create(tenantId);
        var settingsQueries = scope.Services.GetRequiredService<ITenantSettingsQueries>();
        var companyId = (await settingsQueries.GetCurrentCompanyId(ct)) ?? tenant.CompanyId;
        if (companyId is null || companyId == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Aucun identifiant de société (company_id) connu pour le tenant « {tenantId} ».");
        }

        return (realm, companyId.Value.ToString());
    }

    private async Task<bool> TrySendResetEmailAsync(
        KeycloakUserRepresentation user, string temporaryPassword, CancellationToken ct)
    {
        if (!_smtpOptions.IsConfigured || string.IsNullOrWhiteSpace(user.Email))
        {
            return false;
        }

        var consoleUrl = _keycloakOptions.AppBaseUrl.TrimEnd('/');
        var body =
            $"""
            Bonjour,

            Le mot de passe de votre accès à la console Liakont vient d'être réinitialisé.

            Nom d'utilisateur : {user.Username}
            Mot de passe temporaire : {temporaryPassword}

            À votre prochaine connexion, vous devrez choisir un nouveau mot de passe.
            {(string.IsNullOrWhiteSpace(consoleUrl) ? string.Empty : $"Connexion : {consoleUrl}")}

            Si vous n'êtes pas à l'origine de cette demande, contactez votre opérateur Liakont.
            """;

        try
        {
            await _emailTransport.SendAsync(user.Email, "Réinitialisation de votre mot de passe Liakont", body, ct);
            return true;
        }
        catch (Exception ex)
        {
            // L'échec d'envoi n'avorte pas le reset : le mot de passe est remis à l'opérateur (une fois).
            // Tracé sans le contenu du message (le corps porte le secret).
            LogResetEmailFailed(_logger, user.Username ?? "?", ex);
            return false;
        }
    }

    private string AdminBase() => _keycloakOptions.AdminBaseUrl.TrimEnd('/');

    private async Task<HttpClient> CreateAuthenticatedClientAsync(CancellationToken ct)
    {
        var token = await AcquireAdminTokenAsync(ct);
        var client = _httpClientFactory.CreateClient("KeycloakAdmin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Token admin via ROPC (client <c>admin-cli</c>, realm <c>master</c>) — même grant que le socle
    /// <c>KeycloakAdminTokenService</c>, re-acquis ici car ce dernier est INTERNE à l'assembly Common
    /// (pas d'accès depuis le Host, et le socle vendored n'est pas modifié). Sans cache : les opérations
    /// d'administration (lister / réinitialiser) sont ponctuelles. Creds issus des options publiques
    /// <c>KeycloakAdminOptions</c> (jamais versionnées) ; le token ne transite pas par les logs.
    /// </summary>
    private async Task<string> AcquireAdminTokenAsync(CancellationToken ct)
    {
        var tokenUrl = $"{AdminBase()}/realms/master/protocol/openid-connect/token";
        var client = _httpClientFactory.CreateClient("KeycloakAdmin");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = _keycloakOptions.AdminUsername,
            ["password"] = _keycloakOptions.AdminPassword,
        });

        var response = await client.PostAsync(tokenUrl, content, ct);
        await EnsureSuccessAsync(response, "Acquérir le token admin Keycloak", ct);

        var token = await response.Content.ReadFromJsonAsync<AdminTokenResponse>(ct)
            ?? throw new InvalidOperationException("Réponse de token Keycloak vide.");
        return token.AccessToken;
    }

    private sealed class KeycloakUserRepresentation
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("username")]
        public string? Username { get; init; }

        [JsonPropertyName("email")]
        public string? Email { get; init; }

        [JsonPropertyName("firstName")]
        public string? FirstName { get; init; }

        [JsonPropertyName("lastName")]
        public string? LastName { get; init; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; init; }

        [JsonPropertyName("attributes")]
        public Dictionary<string, List<string>>? Attributes { get; init; }
    }

    private sealed class KeycloakRoleRepresentation
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }

    private sealed class AdminTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = default!;
    }
}
