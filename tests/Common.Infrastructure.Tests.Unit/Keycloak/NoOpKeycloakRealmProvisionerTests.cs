namespace Stratum.Common.Infrastructure.Tests.Unit.Keycloak;

using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Keycloak;
using Xunit;

/// <summary>
/// RLM04 (ADR-0021 §1) : le no-op du profil SaaS PARTAGÉ ne crée NI realm NI client par tenant.
/// La preuve « aucun POST /admin/realms » est STRUCTURELLE : <see cref="NoOpKeycloakRealmProvisioner"/>
/// n'a aucune dépendance HTTP (pas d'<c>IHttpClientFactory</c> dans son constructeur) — il lui est
/// donc impossible d'émettre une requête, contrairement à un fake qui se contenterait de ne rien
/// faire. Le résultat <c>AlreadyProvisioned=true</c> force <c>realmCreated=false</c> côté appelant.
/// </summary>
public sealed class NoOpKeycloakRealmProvisionerTests
{
    private static KeycloakRealmProvisionRequest CreateRequest() => new()
    {
        TenantId = "acme",
        DisplayName = "Acme Corp",
        RealmName = "stratum-acme",
        ClientSecret = "test-secret",
        CompanyId = "11111111-1111-4111-a111-111111111111",
        RedirectUris = [],
        WebOrigins = [],
    };

    [Fact]
    public async Task ProvisionRealmAsync_Should_ReturnAlreadyProvisioned_WithoutCreatingARealm()
    {
        var sut = new NoOpKeycloakRealmProvisioner();

        var result = await sut.ProvisionRealmAsync(CreateRequest());

        Assert.True(result.Success);

        // AlreadyProvisioned=true ⇒ realmCreated=false dans TenantProvisioningService.ProvisionAsync ⇒
        // aucun enregistrement de realm ni redirect par tenant (nettoyage vestigial gardé par le seam).
        Assert.True(result.AlreadyProvisioned);
    }

    [Fact]
    public async Task DeleteRealmAsync_Should_BeNoOp()
    {
        var sut = new NoOpKeycloakRealmProvisioner();

        // Ne jette pas et n'émet aucun appel (aucune dépendance HTTP) — la désactivation d'un tenant
        // en realm partagé ne supprime jamais le realm partagé.
        await sut.DeleteRealmAsync("stratum-acme");
    }

    [Fact]
    public async Task AddTenantRedirectUriAsync_Should_BeNoOp()
    {
        var sut = new NoOpKeycloakRealmProvisioner();

        await sut.AddTenantRedirectUriAsync("liakont-dev", "acme");
    }
}
