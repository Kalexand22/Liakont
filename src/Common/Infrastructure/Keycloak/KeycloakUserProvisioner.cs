namespace Stratum.Common.Infrastructure.Keycloak;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Provisions users in an existing Keycloak realm via the Admin REST API.
/// Shares the "KeycloakAdmin" named client and <see cref="KeycloakAdminTokenService"/>
/// with <see cref="KeycloakRealmProvisioner"/> (which is left untouched: it owns realm
/// creation; this service owns per-user operations afterwards).
/// </summary>
internal sealed partial class KeycloakUserProvisioner : IKeycloakUserProvisioner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly KeycloakAdminTokenService _tokenService;
    private readonly KeycloakAdminOptions _options;
    private readonly ILogger<KeycloakUserProvisioner> _logger;

    public KeycloakUserProvisioner(
        IHttpClientFactory httpClientFactory,
        KeycloakAdminTokenService tokenService,
        IOptions<KeycloakAdminOptions> options,
        ILogger<KeycloakUserProvisioner> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenService = tokenService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> FindUserIdByUsernameAsync(
        string realmName, string username, CancellationToken cancellationToken = default)
    {
        var client = await CreateAuthenticatedClientAsync(cancellationToken);
        var url = $"{AdminBase()}/admin/realms/{realmName}/users?username={Uri.EscapeDataString(username)}&exact=true";

        var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, "Find user by username", cancellationToken);

        var users = await response.Content.ReadFromJsonAsync<List<KeycloakUserRow>>(JsonOptions, cancellationToken) ?? [];
        return users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    public async Task<string> CreateUserAsync(
        string realmName, KeycloakUserSpec spec, CancellationToken cancellationToken = default)
    {
        var user = new Dictionary<string, object>
        {
            ["username"] = spec.Username,
            ["email"] = spec.Email,
            ["emailVerified"] = spec.EmailVerified,
            ["enabled"] = true,
        };

        if (!string.IsNullOrWhiteSpace(spec.FirstName))
        {
            user["firstName"] = spec.FirstName;
        }

        if (!string.IsNullOrWhiteSpace(spec.LastName))
        {
            user["lastName"] = spec.LastName;
        }

        if (spec.RequiredActions.Count > 0)
        {
            user["requiredActions"] = spec.RequiredActions;
        }

        if (spec.Attributes.Count > 0)
        {
            user["attributes"] = spec.Attributes.ToDictionary(kvp => kvp.Key, kvp => new[] { kvp.Value });
        }

        var client = await CreateAuthenticatedClientAsync(cancellationToken);
        var url = $"{AdminBase()}/admin/realms/{realmName}/users";

        var response = await client.PostAsJsonAsync(url, user, JsonOptions, cancellationToken);

        // 409 = username OU email déjà pris dans le realm (l'email est unique par realm : un pré-check
        // par username ne suffit pas) → exception TYPÉE pour un refus opérateur propre, jamais un 500.
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var conflictBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new KeycloakUserConflictException(
                $"Keycloak Create user conflict for '{spec.Username}' in realm '{realmName}': {conflictBody}");
        }

        await EnsureSuccessAsync(response, "Create user", cancellationToken);

        var userId = ExtractIdFromLocationHeader(response);
        LogUserCreated(_logger, spec.Username, realmName);
        return userId;
    }

    public async Task SetUserAttributesAsync(
        string realmName,
        string userId,
        IReadOnlyDictionary<string, string> attributes,
        CancellationToken cancellationToken = default)
    {
        // Keycloak's PUT /users/{id} replaces the representation: read-modify-write so
        // attributes set at creation (or by other tools) are preserved.
        var client = await CreateAuthenticatedClientAsync(cancellationToken);
        var url = $"{AdminBase()}/admin/realms/{realmName}/users/{userId}";

        var getResponse = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(getResponse, "Get user before attribute update", cancellationToken);

        var representation = await getResponse.Content
            .ReadFromJsonAsync<Dictionary<string, JsonElement>>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException($"Keycloak returned an empty user representation for '{userId}'.");

        var merged = representation.TryGetValue("attributes", out var existing) && existing.ValueKind == JsonValueKind.Object
            ? existing.Deserialize<Dictionary<string, string[]>>(JsonOptions) ?? []
            : [];

        foreach (var (key, value) in attributes)
        {
            merged[key] = [value];
        }

        // PUT de la représentation COMPLÈTE (attributs réinjectés) : un PUT partiel pourrait, selon
        // la version de Keycloak, réinitialiser les champs absents (email, enabled…) — la
        // représentation lue est autoritative, on ne renvoie jamais moins que ce qu'on a reçu.
        representation["attributes"] = JsonSerializer.SerializeToElement(merged, JsonOptions);
        var putResponse = await client.PutAsJsonAsync(url, representation, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(putResponse, "Set user attributes", cancellationToken);
    }

    public async Task ResetPasswordAsync(
        string realmName, string userId, string password, bool temporary, CancellationToken cancellationToken = default)
    {
        var credential = new Dictionary<string, object>
        {
            ["type"] = "password",
            ["value"] = password,
            ["temporary"] = temporary,
        };

        var client = await CreateAuthenticatedClientAsync(cancellationToken);
        var url = $"{AdminBase()}/admin/realms/{realmName}/users/{userId}/reset-password";

        var response = await client.PutAsJsonAsync(url, credential, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, "Reset user password", cancellationToken);
    }

    public async Task EnsureRealmRoleAsync(
        string realmName, string roleName, string description, CancellationToken cancellationToken = default)
    {
        var role = new Dictionary<string, object>
        {
            ["name"] = roleName,
            ["description"] = description,
        };

        var client = await CreateAuthenticatedClientAsync(cancellationToken);
        var url = $"{AdminBase()}/admin/realms/{realmName}/roles";

        var response = await client.PostAsJsonAsync(url, role, JsonOptions, cancellationToken);

        // 409 Conflict = the role already exists: idempotent success.
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return;
        }

        await EnsureSuccessAsync(response, "Ensure realm role", cancellationToken);
        LogRealmRoleCreated(_logger, roleName, realmName);
    }

    public async Task AssignRealmRolesAsync(
        string realmName, string userId, IReadOnlyList<string> roleNames, CancellationToken cancellationToken = default)
    {
        var client = await CreateAuthenticatedClientAsync(cancellationToken);

        var rolesUrl = $"{AdminBase()}/admin/realms/{realmName}/roles";
        var rolesResponse = await client.GetAsync(rolesUrl, cancellationToken);
        await EnsureSuccessAsync(rolesResponse, "Get realm roles", cancellationToken);

        var allRoles = await rolesResponse.Content.ReadFromJsonAsync<List<KeycloakRoleRow>>(JsonOptions, cancellationToken) ?? [];

        var rolesToAssign = allRoles
            .Where(r => roleNames.Contains(r.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (rolesToAssign.Count == 0)
        {
            return;
        }

        var assignUrl = $"{AdminBase()}/admin/realms/{realmName}/users/{userId}/role-mappings/realm";
        var assignResponse = await client.PostAsJsonAsync(assignUrl, rolesToAssign, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(assignResponse, "Assign realm roles", cancellationToken);
    }

    public async Task DeleteUserAsync(string realmName, string userId, CancellationToken cancellationToken = default)
    {
        var client = await CreateAuthenticatedClientAsync(cancellationToken);
        var url = $"{AdminBase()}/admin/realms/{realmName}/users/{userId}";

        var response = await client.DeleteAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        await EnsureSuccessAsync(response, "Delete user", cancellationToken);
        LogUserDeleted(_logger, userId, realmName);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User '{Username}' created in realm '{RealmName}'")]
    private static partial void LogUserCreated(ILogger logger, string username, string realmName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Realm role '{RoleName}' created in realm '{RealmName}'")]
    private static partial void LogRealmRoleCreated(ILogger logger, string roleName, string realmName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User '{UserId}' deleted from realm '{RealmName}' (compensation)")]
    private static partial void LogUserDeleted(ILogger logger, string userId, string realmName);

    private static string ExtractIdFromLocationHeader(HttpResponseMessage response)
    {
        var location = response.Headers.Location?.ToString()
            ?? throw new InvalidOperationException("Keycloak did not return a Location header after resource creation.");

        return location.Split('/').Last();
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

    private string AdminBase() => _options.AdminBaseUrl.TrimEnd('/');

    private async Task<HttpClient> CreateAuthenticatedClientAsync(CancellationToken ct)
    {
        var token = await _tokenService.GetTokenAsync(ct);
        var client = _httpClientFactory.CreateClient("KeycloakAdmin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed class KeycloakUserRow
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = default!;

        [JsonPropertyName("username")]
        public string Username { get; init; } = default!;
    }

    private sealed class KeycloakRoleRow
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = default!;

        [JsonPropertyName("name")]
        public string Name { get; init; } = default!;
    }
}
