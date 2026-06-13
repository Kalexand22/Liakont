namespace Stratum.Common.Infrastructure.Keycloak;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Provisions Keycloak realms via the Admin REST API.
/// Creates realm and client (with protocol mappers) in sequence — NO realm user is created.
/// The tenant's first user is provisioned separately by the operator wizard (OPS03 lot A,
/// <see cref="IKeycloakUserProvisioner"/>); the instance super-admin lives in the primary realm.
/// Rollback deletes the entire realm (Keycloak cascades all child resources).
/// </summary>
internal sealed partial class KeycloakRealmProvisioner : IKeycloakRealmProvisioner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly KeycloakAdminTokenService _tokenService;
    private readonly KeycloakAdminOptions _options;
    private readonly ILogger<KeycloakRealmProvisioner> _logger;

    public KeycloakRealmProvisioner(
        IHttpClientFactory httpClientFactory,
        KeycloakAdminTokenService tokenService,
        IOptions<KeycloakAdminOptions> options,
        ILogger<KeycloakRealmProvisioner> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenService = tokenService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<KeycloakProvisionResult> ProvisionRealmAsync(
        KeycloakRealmProvisionRequest request,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = _options.AdminBaseUrl.TrimEnd('/');
        var authority = $"{baseUrl}/realms/{request.RealmName}";

        // Idempotency check
        if (await RealmExistsAsync(request.RealmName, cancellationToken))
        {
            LogRealmAlreadyExists(_logger, request.RealmName);
            return KeycloakProvisionResult.Idempotent(request.RealmName, authority);
        }

        LogProvisioningStarted(_logger, request.RealmName);

        try
        {
            await CreateRealmAsync(request, cancellationToken);
            await CreateClientAsync(request, cancellationToken);

            LogProvisioningCompleted(_logger, request.RealmName);
            return KeycloakProvisionResult.Created(request.RealmName, authority, request.ClientSecret);
        }
        catch (Exception ex)
        {
            LogProvisioningFailed(_logger, request.RealmName, ex);
            await DeleteRealmAsync(request.RealmName, CancellationToken.None);
            throw;
        }
    }

    public async Task DeleteRealmAsync(string realmName, CancellationToken cancellationToken = default)
    {
        try
        {
            LogRealmDeletionStarted(_logger, realmName);

            var client = await CreateAuthenticatedClientAsync(cancellationToken);
            var url = $"{_options.AdminBaseUrl.TrimEnd('/')}/admin/realms/{realmName}";

            var response = await client.DeleteAsync(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                LogRealmNotFoundForDeletion(_logger, realmName);
                return;
            }

            response.EnsureSuccessStatusCode();
            LogRealmDeleted(_logger, realmName);
        }
        catch (Exception ex)
        {
            LogRealmDeletionFailed(_logger, realmName, ex);
        }
    }

    public async Task AddTenantRedirectUriAsync(
        string primaryRealmName, string tenantSubdomain, CancellationToken cancellationToken = default)
    {
        try
        {
            var httpClient = await CreateAuthenticatedClientAsync(cancellationToken);
            var baseAdminUrl = _options.AdminBaseUrl.TrimEnd('/');

            // Get the stratum client in the primary realm
            var clientsUrl = $"{baseAdminUrl}/admin/realms/{primaryRealmName}/clients?clientId=stratum";
            var clientsResponse = await httpClient.GetAsync(clientsUrl, cancellationToken);
            await EnsureSuccessAsync(clientsResponse, "Get primary realm clients", cancellationToken);

            var clientsJson = await clientsResponse.Content.ReadAsStringAsync(cancellationToken);
            var internalId = System.Text.RegularExpressions.Regex.Match(clientsJson, @"""id"":""([^""]+)""").Groups[1].Value;

            if (string.IsNullOrEmpty(internalId))
            {
                return;
            }

            // Get current client config
            var clientUrl = $"{baseAdminUrl}/admin/realms/{primaryRealmName}/clients/{internalId}";
            var clientResponse = await httpClient.GetAsync(clientUrl, cancellationToken);
            await EnsureSuccessAsync(clientResponse, "Get primary client config", cancellationToken);

            var clientJson = await clientResponse.Content.ReadAsStringAsync(cancellationToken);

            // Parse redirect URIs and add the new subdomain patterns
            var appBaseUrl = _options.AppBaseUrl.TrimEnd('/');
            var uri = new Uri(appBaseUrl);
            var newRedirect = $"{uri.Scheme}://{tenantSubdomain}.{uri.Host}:{uri.Port}/*";

            // Check if already present
            if (clientJson.Contains(tenantSubdomain, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Extract current redirectUris array and append
            var redirectMatch = System.Text.RegularExpressions.Regex.Match(clientJson, @"""redirectUris"":\[([^\]]*)\]");
            var currentUris = redirectMatch.Success ? redirectMatch.Groups[1].Value : string.Empty;
            var newUris = string.IsNullOrEmpty(currentUris)
                ? $"\"{newRedirect}\""
                : $"{currentUris},\"{newRedirect}\"";

            var updatePayload = $"{{\"clientId\":\"stratum\",\"redirectUris\":[{newUris}],\"webOrigins\":[\"+\"]}}";

            var updateResponse = await httpClient.PutAsync(
                clientUrl,
                new StringContent(updatePayload, System.Text.Encoding.UTF8, "application/json"),
                cancellationToken);
            await EnsureSuccessAsync(updateResponse, "Update primary client redirect URIs", cancellationToken);

            LogRedirectUriAdded(_logger, tenantSubdomain, primaryRealmName);
        }
        catch (Exception ex)
        {
            LogRedirectUriAddFailed(_logger, tenantSubdomain, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Redirect URI for '{Subdomain}' added to primary realm '{RealmName}'")]
    private static partial void LogRedirectUriAdded(ILogger logger, string subdomain, string realmName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to add redirect URI for '{Subdomain}' — browser login may require manual config")]
    private static partial void LogRedirectUriAddFailed(ILogger logger, string subdomain, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Provisioning Keycloak realm '{RealmName}'")]
    private static partial void LogProvisioningStarted(ILogger logger, string realmName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Keycloak realm '{RealmName}' already exists — returning idempotent success")]
    private static partial void LogRealmAlreadyExists(ILogger logger, string realmName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Keycloak realm '{RealmName}' created")]
    private static partial void LogRealmCreated(ILogger logger, string realmName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "OIDC client 'stratum' created in realm '{RealmName}'")]
    private static partial void LogClientCreated(ILogger logger, string realmName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Keycloak realm '{RealmName}' provisioning completed")]
    private static partial void LogProvisioningCompleted(ILogger logger, string realmName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Keycloak realm '{RealmName}' provisioning failed")]
    private static partial void LogProvisioningFailed(ILogger logger, string realmName, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleting Keycloak realm '{RealmName}' (rollback)")]
    private static partial void LogRealmDeletionStarted(ILogger logger, string realmName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Keycloak realm '{RealmName}' deleted")]
    private static partial void LogRealmDeleted(ILogger logger, string realmName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keycloak realm '{RealmName}' not found for deletion")]
    private static partial void LogRealmNotFoundForDeletion(ILogger logger, string realmName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to delete Keycloak realm '{RealmName}' — manual cleanup may be required")]
    private static partial void LogRealmDeletionFailed(ILogger logger, string realmName, Exception exception);

    private static List<Dictionary<string, object>> BuildProtocolMappers(string tenantId, string companyId)
    {
        return
        [

            // company_id is HARDCODED at the client level (one tenant = one company): every user of
            // the realm carries the claim without needing a per-user attribute. An attribute mapper
            // here used to leave users without the claim unless someone remembered to set the
            // attribute — which broke all company-scoped data access for freshly provisioned admins.
            BuildHardcodedMapper("company_id", "company_id", companyId),
            BuildAttributeMapper("stratum_user_id", "stratum_user_id", "stratum_user_id"),
            BuildHardcodedMapper("tenant_id", "tenant_id", tenantId),
            BuildRealmRoleMapper(),
            BuildAudienceMapper(),
        ];
    }

    private static Dictionary<string, object> BuildRealmRoleMapper()
    {
        return new Dictionary<string, object>
        {
            ["name"] = "realm roles",
            ["protocol"] = "openid-connect",
            ["protocolMapper"] = "oidc-usermodel-realm-role-mapper",
            ["config"] = new Dictionary<string, string>
            {
                ["claim.name"] = "roles",
                ["jsonType.label"] = "String",
                ["multivalued"] = "true",
                ["id.token.claim"] = "true",
                ["access.token.claim"] = "true",
                ["userinfo.token.claim"] = "true",
            },
        };
    }

    private static Dictionary<string, object> BuildAudienceMapper()
    {
        return new Dictionary<string, object>
        {
            ["name"] = "audience",
            ["protocol"] = "openid-connect",
            ["protocolMapper"] = "oidc-audience-mapper",
            ["config"] = new Dictionary<string, string>
            {
                ["included.client.audience"] = "stratum",
                ["id.token.claim"] = "false",
                ["access.token.claim"] = "true",
            },
        };
    }

    private static Dictionary<string, object> BuildAttributeMapper(string name, string userAttribute, string claimName)
    {
        return new Dictionary<string, object>
        {
            ["name"] = name,
            ["protocol"] = "openid-connect",
            ["protocolMapper"] = "oidc-usermodel-attribute-mapper",
            ["config"] = new Dictionary<string, string>
            {
                ["user.attribute"] = userAttribute,
                ["claim.name"] = claimName,
                ["jsonType.label"] = "String",
                ["id.token.claim"] = "true",
                ["access.token.claim"] = "true",
                ["userinfo.token.claim"] = "true",
            },
        };
    }

    private static Dictionary<string, object> BuildHardcodedMapper(string name, string claimName, string claimValue)
    {
        return new Dictionary<string, object>
        {
            ["name"] = name,
            ["protocol"] = "openid-connect",
            ["protocolMapper"] = "oidc-hardcoded-claim-mapper",
            ["config"] = new Dictionary<string, string>
            {
                ["claim.value"] = claimValue,
                ["claim.name"] = claimName,
                ["jsonType.label"] = "String",
                ["id.token.claim"] = "true",
                ["access.token.claim"] = "true",
                ["userinfo.token.claim"] = "true",
            },
        };
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Keycloak {operation} failed ({response.StatusCode}): {body}");
        }
    }

    private async Task<bool> RealmExistsAsync(string realmName, CancellationToken ct)
    {
        var client = await CreateAuthenticatedClientAsync(ct);
        var url = $"{_options.AdminBaseUrl.TrimEnd('/')}/admin/realms/{realmName}";

        var response = await client.GetAsync(url, ct);
        return response.StatusCode != HttpStatusCode.NotFound;
    }

    private async Task CreateRealmAsync(KeycloakRealmProvisionRequest request, CancellationToken ct)
    {
        var realm = new Dictionary<string, object>
        {
            ["id"] = request.RealmName,
            ["realm"] = request.RealmName,
            ["displayName"] = request.DisplayName,
            ["enabled"] = true,
            ["sslRequired"] = "external",
            ["loginTheme"] = "stratum",
            ["internationalizationEnabled"] = true,
            ["supportedLocales"] = new[] { "fr", "en" },
            ["defaultLocale"] = "fr",
            ["loginWithEmailAllowed"] = true,
            ["duplicateEmailsAllowed"] = false,
            ["resetPasswordAllowed"] = true,
            ["editUsernameAllowed"] = false,
            ["bruteForceProtected"] = true,
            ["maxFailureWaitSeconds"] = 900,
            ["failureFactor"] = 5,
            ["accessTokenLifespan"] = 300,
            ["ssoSessionIdleTimeout"] = 28800,
            ["ssoSessionMaxLifespan"] = 36000,
            ["roles"] = new Dictionary<string, object>
            {
                ["realm"] = new[]
                {
                    new { name = StratumRoles.User, description = "Default Stratum user role" },
                    new { name = StratumRoles.Admin, description = "Stratum administrator role" },
                    new { name = StratumRoles.Volunteer, description = "Volunteer with limited access (schedule read, attendance write)" },
                    new { name = StratumRoles.SystemAdmin, description = "Stratum system administrator" },
                },
            },
            ["defaultRoles"] = new[] { StratumRoles.User },
        };

        var client = await CreateAuthenticatedClientAsync(ct);
        var url = $"{_options.AdminBaseUrl.TrimEnd('/')}/admin/realms";

        var response = await client.PostAsJsonAsync(url, realm, JsonOptions, ct);
        await EnsureSuccessAsync(response, "Create realm", ct);

        LogRealmCreated(_logger, request.RealmName);
    }

    private async Task CreateClientAsync(KeycloakRealmProvisionRequest request, CancellationToken ct)
    {
        var oidcClient = new Dictionary<string, object>
        {
            ["clientId"] = "stratum",
            ["name"] = "Stratum ERP",
            ["enabled"] = true,
            ["clientAuthenticatorType"] = "client-secret",
            ["secret"] = request.ClientSecret,
            ["protocol"] = "openid-connect",
            ["publicClient"] = false,
            ["standardFlowEnabled"] = true,
            ["implicitFlowEnabled"] = false,
            ["directAccessGrantsEnabled"] = false,
            ["serviceAccountsEnabled"] = false,
            ["fullScopeAllowed"] = true,
            ["redirectUris"] = request.RedirectUris,
            ["webOrigins"] = request.WebOrigins,
            ["defaultClientScopes"] = new[] { "openid", "profile", "email" },
            ["protocolMappers"] = BuildProtocolMappers(request.TenantId, request.CompanyId),
        };

        var client = await CreateAuthenticatedClientAsync(ct);
        var url = $"{_options.AdminBaseUrl.TrimEnd('/')}/admin/realms/{request.RealmName}/clients";

        var response = await client.PostAsJsonAsync(url, oidcClient, JsonOptions, ct);
        await EnsureSuccessAsync(response, "Create OIDC client", ct);

        LogClientCreated(_logger, request.RealmName);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(CancellationToken ct)
    {
        var token = await _tokenService.GetTokenAsync(ct);
        var client = _httpClientFactory.CreateClient("KeycloakAdmin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
