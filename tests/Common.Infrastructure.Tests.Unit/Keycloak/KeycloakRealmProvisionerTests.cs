namespace Stratum.Common.Infrastructure.Tests.Unit.Keycloak;

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Keycloak;
using Xunit;

public sealed class KeycloakRealmProvisionerTests : IDisposable
{
    private static readonly string[] DefaultRedirectUris = ["https://localhost:55995/*"];
    private static readonly string[] DefaultWebOrigins = ["https://localhost:55995"];
    private static readonly string[] WildcardOrigins = ["+"];

    private readonly KeycloakAdminOptions _options = new()
    {
        AdminBaseUrl = "http://localhost:8080",
        AdminUsername = "admin",
        AdminPassword = "admin-secret",
        AppBaseUrl = "https://localhost:55995",
        PrimaryRealmName = "stratum-dev",
    };

    private readonly FakeHttpMessageHandler _handler = new();
    private KeycloakAdminTokenService? _tokenService;

    private static KeycloakRealmProvisionRequest CreateRequest(string realmName = "stratum-acme") => new()
    {
        TenantId = "acme",
        DisplayName = "Acme Corp",
        RealmName = realmName,
        ClientSecret = "test-secret",
        AdminEmail = "admin@acme.com",
        AdminUsername = "admin",
        AdminPassword = "P@ssw0rd!",
        StratumUserId = "00000000-0000-0000-0000-000000000001",
        CompanyId = "11111111-1111-4111-a111-111111111111",
        RedirectUris = DefaultRedirectUris,
        WebOrigins = DefaultWebOrigins,
    };

    public void Dispose()
    {
        _tokenService?.Dispose();
    }

    [Fact]
    public async Task ProvisionRealmAsync_Should_ReturnIdempotent_When_RealmAlreadyExists()
    {
        EnqueueTokenResponse();
        _handler.EnqueueResponse(HttpStatusCode.OK, "{}");
        var sut = CreateSut();

        var result = await sut.ProvisionRealmAsync(CreateRequest());

        Assert.True(result.Success);
        Assert.True(result.AlreadyProvisioned);
        Assert.Equal("stratum-acme", result.RealmName);
    }

    [Fact]
    public async Task ProvisionRealmAsync_Should_CreateRealmClientAndUser_When_RealmDoesNotExist()
    {
        EnqueueTokenResponse();
        _handler.EnqueueResponse(HttpStatusCode.NotFound, string.Empty);
        _handler.EnqueueResponse(HttpStatusCode.Created, string.Empty);
        _handler.EnqueueResponse(HttpStatusCode.Created, string.Empty);
        _handler.EnqueueResponseWithLocation(
            HttpStatusCode.Created,
            string.Empty,
            "/admin/realms/stratum-acme/users/user-123");
        _handler.EnqueueResponse(HttpStatusCode.NoContent, string.Empty);
        _handler.EnqueueResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new[]
        {
            new { id = "role-1", name = "stratum-user" },
            new { id = "role-2", name = "stratum-admin" },
            new { id = "role-3", name = "SystemAdmin" },
        }));
        _handler.EnqueueResponse(HttpStatusCode.NoContent, string.Empty);
        var sut = CreateSut();

        var result = await sut.ProvisionRealmAsync(CreateRequest());

        Assert.True(result.Success);
        Assert.False(result.AlreadyProvisioned);
        Assert.Equal("stratum-acme", result.RealmName);
        Assert.Equal("http://localhost:8080/realms/stratum-acme", result.Authority);
        Assert.Equal("test-secret", result.ClientSecret);
    }

    [Fact]
    public async Task ProvisionRealmAsync_Should_Emit_CompanyId_As_Hardcoded_Client_Mapper()
    {
        // OPS03 lot A : company_id est un mapper HARDCODÉ au niveau client (un tenant = une société),
        // pas un mapper d'attribut utilisateur — sinon tout utilisateur sans attribut perd son scope
        // de données (le piège de l'admin fraîchement provisionné).
        EnqueueTokenResponse();
        _handler.EnqueueResponse(HttpStatusCode.NotFound, string.Empty);
        _handler.EnqueueResponse(HttpStatusCode.Created, string.Empty);
        _handler.EnqueueResponse(HttpStatusCode.Created, string.Empty);
        _handler.EnqueueResponseWithLocation(HttpStatusCode.Created, string.Empty, "/admin/realms/stratum-acme/users/user-123");
        _handler.EnqueueResponse(HttpStatusCode.NoContent, string.Empty);
        _handler.EnqueueResponse(HttpStatusCode.OK, "[]");
        var sut = CreateSut();

        await sut.ProvisionRealmAsync(CreateRequest());

        // Requête 3 (index 3 : token, realm-exists, create-realm, create-client) = client OIDC.
        var clientBody = _handler.AllRequestBodies[3];
        Assert.NotNull(clientBody);
        using var doc = JsonDocument.Parse(clientBody);
        var mappers = doc.RootElement.GetProperty("protocolMappers").EnumerateArray().ToList();
        var companyMapper = mappers.Single(m => m.GetProperty("name").GetString() == "company_id");
        Assert.Equal("oidc-hardcoded-claim-mapper", companyMapper.GetProperty("protocolMapper").GetString());
        Assert.Equal(
            "11111111-1111-4111-a111-111111111111",
            companyMapper.GetProperty("config").GetProperty("claim.value").GetString());
    }

    [Fact]
    public async Task ProvisionRealmAsync_Should_Create_Admin_With_Forced_Password_Change()
    {
        // OPS03 lot A : mot de passe TEMPORAIRE réel (temporary=true) + action UPDATE_PASSWORD —
        // l'ancien credential permanent contredisait son propre commentaire « temporary ».
        EnqueueTokenResponse();
        _handler.EnqueueResponse(HttpStatusCode.NotFound, string.Empty);
        _handler.EnqueueResponse(HttpStatusCode.Created, string.Empty);
        _handler.EnqueueResponse(HttpStatusCode.Created, string.Empty);
        _handler.EnqueueResponseWithLocation(HttpStatusCode.Created, string.Empty, "/admin/realms/stratum-acme/users/user-123");
        _handler.EnqueueResponse(HttpStatusCode.NoContent, string.Empty);
        _handler.EnqueueResponse(HttpStatusCode.OK, "[]");
        var sut = CreateSut();

        await sut.ProvisionRealmAsync(CreateRequest());

        // Requête 4 = création de l'admin ; requête 5 = reset-password.
        using var userDoc = JsonDocument.Parse(_handler.AllRequestBodies[4]!);
        var requiredActions = userDoc.RootElement.GetProperty("requiredActions").EnumerateArray()
            .Select(a => a.GetString()).ToList();
        Assert.Contains("UPDATE_PASSWORD", requiredActions);

        using var credentialDoc = JsonDocument.Parse(_handler.AllRequestBodies[5]!);
        Assert.True(credentialDoc.RootElement.GetProperty("temporary").GetBoolean());
    }

    [Fact]
    public async Task ProvisionRealmAsync_Should_RollbackAndRethrow_When_ClientCreationFails()
    {
        EnqueueTokenResponse();
        _handler.EnqueueResponse(HttpStatusCode.NotFound, string.Empty);
        _handler.EnqueueResponse(HttpStatusCode.Created, string.Empty);
        _handler.EnqueueResponse(HttpStatusCode.InternalServerError, "Server error");

        // After failure, DeleteRealmAsync is called (rollback)
        _handler.EnqueueResponse(HttpStatusCode.NoContent, string.Empty);
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ProvisionRealmAsync(CreateRequest()));
    }

    [Fact]
    public async Task DeleteRealmAsync_Should_Succeed_When_RealmExists()
    {
        EnqueueTokenResponse();
        _handler.EnqueueResponse(HttpStatusCode.NoContent, string.Empty);
        var sut = CreateSut();

        await sut.DeleteRealmAsync("stratum-acme");
    }

    [Fact]
    public async Task DeleteRealmAsync_Should_NotThrow_When_RealmNotFound()
    {
        EnqueueTokenResponse();
        _handler.EnqueueResponse(HttpStatusCode.NotFound, string.Empty);
        var sut = CreateSut();

        await sut.DeleteRealmAsync("nonexistent-realm");
    }

    [Fact]
    public async Task DeleteRealmAsync_Should_NotThrow_When_ServerError()
    {
        EnqueueTokenResponse();
        _handler.EnqueueResponse(HttpStatusCode.InternalServerError, "error");
        var sut = CreateSut();

        await sut.DeleteRealmAsync("stratum-acme");
    }

    [Fact]
    public async Task AddTenantRedirectUriAsync_Should_UpdateClient_When_SubdomainNotPresent()
    {
        var clientsResponse = JsonSerializer.Serialize(new[]
        {
            new { id = "client-internal-id", clientId = "stratum" },
        });
        var clientConfig = JsonSerializer.Serialize(new
        {
            clientId = "stratum",
            redirectUris = DefaultRedirectUris,
            webOrigins = WildcardOrigins,
        });

        EnqueueTokenResponse();
        _handler.EnqueueResponse(HttpStatusCode.OK, clientsResponse);
        _handler.EnqueueResponse(HttpStatusCode.OK, clientConfig);
        _handler.EnqueueResponse(HttpStatusCode.NoContent, string.Empty);
        var sut = CreateSut();

        await sut.AddTenantRedirectUriAsync("stratum-dev", "acme");
    }

    [Fact]
    public async Task AddTenantRedirectUriAsync_Should_Skip_When_SubdomainAlreadyPresent()
    {
        var clientsResponse = JsonSerializer.Serialize(new[]
        {
            new { id = "client-internal-id", clientId = "stratum" },
        });
        var clientConfig = "{\"clientId\":\"stratum\",\"redirectUris\":[\"https://acme.localhost:55995/*\"],\"webOrigins\":[\"+\"]}";

        EnqueueTokenResponse();
        _handler.EnqueueResponse(HttpStatusCode.OK, clientsResponse);
        _handler.EnqueueResponse(HttpStatusCode.OK, clientConfig);
        var sut = CreateSut();

        await sut.AddTenantRedirectUriAsync("stratum-dev", "acme");

        // Token call + 2 GETs = 3 total. No PUT.
        Assert.Equal(3, _handler.CallCount);
    }

    [Fact]
    public async Task AddTenantRedirectUriAsync_Should_NotThrow_When_ClientNotFound()
    {
        EnqueueTokenResponse();
        _handler.EnqueueResponse(HttpStatusCode.OK, "[]");
        var sut = CreateSut();

        await sut.AddTenantRedirectUriAsync("stratum-dev", "acme");
    }

    [Fact]
    public async Task ProvisionRealmAsync_Should_TreatNon404AsExists_When_RealmCheckReturns500()
    {
        // Current production behavior: any non-404 from GET /admin/realms/{name}
        // is treated as "realm exists" → idempotent return. This documents that behavior.
        EnqueueTokenResponse();
        _handler.EnqueueResponse(HttpStatusCode.InternalServerError, "server error");
        var sut = CreateSut();

        var result = await sut.ProvisionRealmAsync(CreateRequest());

        Assert.True(result.Success);
        Assert.True(result.AlreadyProvisioned);
    }

    [Fact]
    public async Task ProvisionRealmAsync_Should_UseAuthBearerToken()
    {
        EnqueueTokenResponse();
        _handler.EnqueueResponse(HttpStatusCode.OK, "{}");
        var sut = CreateSut();

        await sut.ProvisionRealmAsync(CreateRequest());

        // The realm-exists check (second request) should have a Bearer token
        Assert.True(_handler.AllRequests.Count >= 2);
        var realmCheckAuth = _handler.AllRequests[1].Auth;
        Assert.NotNull(realmCheckAuth);
        Assert.StartsWith("Bearer ", realmCheckAuth);
    }

    private KeycloakRealmProvisioner CreateSut()
    {
        var factory = new FakeHttpClientFactory(_handler);
        _tokenService = new KeycloakAdminTokenService(
            factory,
            Options.Create(_options),
            NullLogger<KeycloakAdminTokenService>.Instance);

        return new KeycloakRealmProvisioner(
            factory,
            _tokenService,
            Options.Create(_options),
            NullLogger<KeycloakRealmProvisioner>.Instance);
    }

    private void EnqueueTokenResponse()
    {
        _handler.EnqueueResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            access_token = "fake-admin-token",
            expires_in = 300,
        }));
    }
}
