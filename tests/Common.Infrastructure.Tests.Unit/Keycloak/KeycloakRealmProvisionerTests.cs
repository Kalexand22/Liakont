namespace Stratum.Common.Infrastructure.Tests.Unit.Keycloak;

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Keycloak;
using Xunit;

/// <summary>
/// Exerce le vrai <see cref="KeycloakRealmProvisioner"/>, qui — depuis RLM04 (ADR-0021 §1) — n'est
/// câblé QUE dans le profil <b>dédié mono-tenant</b> (<c>Keycloak:DedicatedRealmPerTenant=true</c>).
/// Le profil SaaS <b>partagé</b> (défaut) utilise <see cref="NoOpKeycloakRealmProvisioner"/> et ne crée
/// NI realm NI client par tenant (cf. <see cref="NoOpKeycloakRealmProvisionerTests"/> et
/// <see cref="Database.RealmProvisionerRegistrationTests"/>). Le mapper <c>company_id</c> HARDCODÉ au
/// niveau client (un realm = une société) n'est donc valable que pour le dédié — JAMAIS pour le partagé,
/// où <c>company_id</c> est un mapper d'ATTRIBUT par-utilisateur (sinon tous les jetons porteraient la
/// même valeur = isolation nulle). Voir provenance §4.24/§4.28.
/// </summary>
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
    public async Task ProvisionRealmAsync_Should_CreateRealmAndClient_WithNoUser_When_RealmDoesNotExist()
    {
        // Le realm naît avec realm + client OIDC et AUCUN utilisateur (le premier utilisateur du
        // tenant est provisionné séparément par l'assistant opérateur, OPS03 lot A) : seules 4 requêtes
        // sortent (token, GET realm-exists, POST realm, POST client) — jamais de création d'utilisateur.
        EnqueueTokenResponse();
        _handler.EnqueueResponse(HttpStatusCode.NotFound, string.Empty);
        _handler.EnqueueResponse(HttpStatusCode.Created, string.Empty);
        _handler.EnqueueResponse(HttpStatusCode.Created, string.Empty);
        var sut = CreateSut();

        var result = await sut.ProvisionRealmAsync(CreateRequest());

        Assert.True(result.Success);
        Assert.False(result.AlreadyProvisioned);
        Assert.Equal("stratum-acme", result.RealmName);
        Assert.Equal("http://localhost:8080/realms/stratum-acme", result.Authority);
        Assert.Equal("test-secret", result.ClientSecret);
        Assert.Equal(4, _handler.CallCount);
    }

    [Fact]
    public async Task ProvisionRealmAsync_DedicatedProfile_Should_Emit_CompanyId_As_Hardcoded_Client_Mapper()
    {
        // Profil DÉDIÉ mono-tenant UNIQUEMENT (RLM04, ADR-0021 §1) — JAMAIS pour le profil partagé
        // (qui passe par le no-op et ne crée aucun realm). Dans le dédié, un realm = une société :
        // company_id est un mapper HARDCODÉ au niveau client (pas un mapper d'attribut utilisateur)
        // — sinon tout utilisateur sans attribut perd son scope de données (le piège de l'admin
        // fraîchement provisionné). En realm PARTAGÉ ce mapper serait une faute (tous les jetons
        // porteraient la même valeur) : c'est le mapper d'attribut par-utilisateur qui s'applique.
        EnqueueTokenResponse();
        _handler.EnqueueResponse(HttpStatusCode.NotFound, string.Empty);
        _handler.EnqueueResponse(HttpStatusCode.Created, string.Empty);
        _handler.EnqueueResponse(HttpStatusCode.Created, string.Empty);
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
