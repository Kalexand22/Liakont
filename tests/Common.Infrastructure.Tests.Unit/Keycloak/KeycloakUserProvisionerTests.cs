namespace Stratum.Common.Infrastructure.Tests.Unit.Keycloak;

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Keycloak;
using Xunit;

/// <summary>
/// Tests du seam de provisioning d'UTILISATEUR dans un realm existant (OPS03 lot A) :
/// création (id extrait du header Location), idempotence du rôle (409 = succès),
/// fusion des attributs (read-modify-write), suppression-compensation tolérante au 404.
/// </summary>
public sealed class KeycloakUserProvisionerTests : IDisposable
{
    private readonly KeycloakAdminOptions _options = new()
    {
        AdminBaseUrl = "http://localhost:8080",
        AdminUsername = "admin",
        AdminPassword = "admin-secret",
        AppBaseUrl = "https://localhost:55995",
    };

    private readonly FakeHttpMessageHandler _handler = new();
    private KeycloakAdminTokenService? _tokenService;

    public void Dispose()
    {
        _tokenService?.Dispose();
    }

    [Fact]
    public async Task CreateUserAsync_Should_Return_Id_From_Location_Header()
    {
        EnqueueTokenResponse();
        _handler.EnqueueResponseWithLocation(HttpStatusCode.Created, string.Empty, "/admin/realms/stratum-acme/users/kc-42");
        var sut = CreateSut();

        var id = await sut.CreateUserAsync("stratum-acme", new KeycloakUserSpec
        {
            Username = "jdupont",
            Email = "j.dupont@exemple.test",
            RequiredActions = ["UPDATE_PASSWORD"],
        });

        Assert.Equal("kc-42", id);

        using var doc = JsonDocument.Parse(_handler.AllRequestBodies[1]!);
        Assert.Equal("jdupont", doc.RootElement.GetProperty("username").GetString());
        var actions = doc.RootElement.GetProperty("requiredActions").EnumerateArray().Select(a => a.GetString());
        Assert.Contains("UPDATE_PASSWORD", actions);
    }

    [Fact]
    public async Task EnsureRealmRoleAsync_Should_Treat_Conflict_As_Success()
    {
        EnqueueTokenResponse();
        _handler.EnqueueResponse(HttpStatusCode.Conflict, "{\"errorMessage\":\"Role exists\"}");
        var sut = CreateSut();

        await sut.EnsureRealmRoleAsync("stratum-acme", "operateur", "desc");
    }

    [Fact]
    public async Task SetUserAttributesAsync_Should_Merge_With_Existing_Attributes()
    {
        EnqueueTokenResponse();
        _handler.EnqueueResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            id = "kc-42",
            username = "jdupont",
            attributes = new Dictionary<string, string[]> { ["existing"] = ["kept"] },
        }));
        _handler.EnqueueResponse(HttpStatusCode.NoContent, string.Empty);
        var sut = CreateSut();

        await sut.SetUserAttributesAsync(
            "stratum-acme", "kc-42", new Dictionary<string, string> { ["stratum_user_id"] = "u-1" });

        using var doc = JsonDocument.Parse(_handler.AllRequestBodies[2]!);
        var attributes = doc.RootElement.GetProperty("attributes");
        Assert.Equal("kept", attributes.GetProperty("existing")[0].GetString());
        Assert.Equal("u-1", attributes.GetProperty("stratum_user_id")[0].GetString());

        // PUT de la représentation COMPLÈTE : un PUT partiel pourrait réinitialiser les champs
        // absents (username, email, enabled) selon la version de Keycloak.
        Assert.Equal("jdupont", doc.RootElement.GetProperty("username").GetString());
    }

    [Fact]
    public async Task DeleteUserAsync_Should_NotThrow_When_UserNotFound()
    {
        EnqueueTokenResponse();
        _handler.EnqueueResponse(HttpStatusCode.NotFound, string.Empty);
        var sut = CreateSut();

        await sut.DeleteUserAsync("stratum-acme", "kc-42");
    }

    [Fact]
    public async Task FindUserIdByUsernameAsync_Should_Return_Null_When_No_Match()
    {
        EnqueueTokenResponse();
        _handler.EnqueueResponse(HttpStatusCode.OK, "[]");
        var sut = CreateSut();

        var id = await sut.FindUserIdByUsernameAsync("stratum-acme", "absent");

        Assert.Null(id);
    }

    [Fact]
    public async Task FindUserIdByUsernameAsync_Should_Match_Exact_Username_Only()
    {
        // Keycloak peut renvoyer des correspondances partielles malgré exact=true selon la version :
        // le filtre client ne retient que l'égalité stricte (insensible à la casse).
        EnqueueTokenResponse();
        _handler.EnqueueResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new[]
        {
            new { id = "kc-1", username = "jdupont-bis" },
            new { id = "kc-2", username = "JDupont" },
        }));
        var sut = CreateSut();

        var id = await sut.FindUserIdByUsernameAsync("stratum-acme", "jdupont");

        Assert.Equal("kc-2", id);
    }

    private KeycloakUserProvisioner CreateSut()
    {
        var factory = new FakeHttpClientFactory(_handler);
        _tokenService = new KeycloakAdminTokenService(
            factory,
            Options.Create(_options),
            NullLogger<KeycloakAdminTokenService>.Instance);

        return new KeycloakUserProvisioner(
            factory,
            _tokenService,
            Options.Create(_options),
            NullLogger<KeycloakUserProvisioner>.Instance);
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
