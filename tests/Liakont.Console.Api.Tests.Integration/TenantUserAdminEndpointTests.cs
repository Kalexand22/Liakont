namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Npgsql;
using Xunit;

/// <summary>
/// Tests d'intégration in-process du provisioning d'utilisateur de tenant (OPS03 lot A,
/// <c>POST /api/v1/admin/tenants/{tenantId}/users</c>) : garde <c>SystemAdmin</c> (401/403), tenant
/// inconnu (404), rôle inventé (400), et chemin nominal (201) qui crée le compte IdP (fake — attributs
/// <c>stratum_user_id</c>/<c>company_id</c> posés, rôle standard assigné) ET le compte applicatif
/// <c>identity.users</c> dans la base du TENANT cible. Sans SMTP configuré (le harness n'en a pas),
/// le mot de passe temporaire est retourné UNE fois. Cible le tenant dédié
/// <see cref="ConsoleApiFactory.TenantUserProv"/> (registre porteur d'un company_id, aucun profil).
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class TenantUserAdminEndpointTests
{
    private const string SystemAdminRole = "SystemAdmin";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ConsoleApiFactory _factory;

    public TenantUserAdminEndpointTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    private static string UsersPath(string tenantId) => $"/api/v1/admin/tenants/{tenantId}/users";

    private static object Body(string username = "jdupont", string role = "operateur") => new
    {
        email = $"{username}@exemple.test",
        username,
        displayName = "Jeanne Dupont",
        role,
    };

    [Fact]
    public async Task CreateUser_Without_Authentication_Returns_401()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantUserProv);

        var response = await client.PostAsJsonAsync(UsersPath(ConsoleApiFactory.TenantUserProv), Body());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateUser_Without_SystemAdmin_Role_Returns_403()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantUserProv, ConsoleApiFactory.SystemAdminUserId);

        var response = await client.PostAsJsonAsync(UsersPath(ConsoleApiFactory.TenantUserProv), Body());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateUser_For_Unknown_Tenant_Returns_404()
    {
        using var client = AdminClient();

        var response = await client.PostAsJsonAsync(UsersPath("tenant-inexistant"), Body());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateUser_With_Invented_Role_Returns_400_With_The_Valid_Roles()
    {
        using var client = AdminClient();

        var response = await client.PostAsJsonAsync(
            UsersPath(ConsoleApiFactory.TenantUserProv), Body(username: "jrole", role: "patron"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("lecture", "le refus liste les rôles standard valides");
    }

    /// <summary>
    /// Ce test est AUSSI le verrou du contrat « contexte HTTP nul = provisioning système » du
    /// <c>CreateUserHandler</c> socle : le service suspend l'acteur ambiant le temps du Send (l'opérateur
    /// d'instance ne porte jamais <c>identity.users.create</c> du tenant cible) et ce 201 passe par le
    /// VRAI handler. Si le socle durcit ce chemin (garde même sans acteur), ce test casse visiblement —
    /// jamais une rupture silencieuse du provisioning.
    /// </summary>
    [Fact]
    public async Task CreateUser_Nominal_Creates_Idp_And_Application_Accounts_And_Returns_The_Password_Once()
    {
        using var client = AdminClient();

        var response = await client.PostAsJsonAsync(
            UsersPath(ConsoleApiFactory.TenantUserProv), Body(username: "jnominal", role: "parametrage"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreatedUserResponse>(JsonOptions);
        created!.UserId.Should().NotBeEmpty();
        created.IdpUserId.Should().NotBeNullOrWhiteSpace();
        created.InvitationEmailSent.Should().BeFalse("le harness n'a pas de SMTP configuré");
        created.TemporaryPassword.Should().NotBeNullOrWhiteSpace("sans SMTP, le mot de passe est remis UNE fois");

        // Compte IdP (fake) : attributs de scope posés + rôle standard assigné + mot de passe temporaire.
        var idpUser = _factory.KeycloakUsers.Users.Single(u => u.Id == created.IdpUserId);
        idpUser.Spec.RequiredActions.Should().Contain("UPDATE_PASSWORD");
        idpUser.Attributes["stratum_user_id"].Should().Be(created.UserId.ToString());
        idpUser.Attributes["company_id"].Should().Be(
            ConsoleApiFactory.TenantUserProvCompanyId.ToString(),
            "sans profil, le company_id vient du REGISTRE du tenant");
        idpUser.Roles.Should().Contain("parametrage");
        idpUser.LastPassword.Should().Be(created.TemporaryPassword);
        idpUser.LastPasswordTemporary.Should().BeTrue();
        idpUser.Deleted.Should().BeFalse();

        // Compte applicatif dans la base du TENANT cible : ExternalId = sub IdP (le sync OIDC le retrouvera).
        await using var conn = new NpgsqlConnection(_factory.TenantConnectionString(ConsoleApiFactory.TenantUserProv));
        await conn.OpenAsync();
        var externalId = await conn.ExecuteScalarAsync<string?>(
            "SELECT external_id FROM identity.users WHERE id = @Id",
            new { Id = created.UserId });
        externalId.Should().Be(created.IdpUserId);
    }

    [Fact]
    public async Task CreateUser_Twice_With_The_Same_Username_Returns_409()
    {
        using var client = AdminClient();

        var first = await client.PostAsJsonAsync(
            UsersPath(ConsoleApiFactory.TenantUserProv), Body(username: "jdouble", role: "lecture"));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync(
            UsersPath(ConsoleApiFactory.TenantUserProv), Body(username: "jdouble", role: "lecture"));
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private HttpClient AdminClient() =>
        _factory.CreateClient(ConsoleApiFactory.TenantUserProv, ConsoleApiFactory.SystemAdminUserId, roles: SystemAdminRole);

    private sealed record CreatedUserResponse(
        Guid UserId, string IdpUserId, bool InvitationEmailSent, string? TemporaryPassword);
}
