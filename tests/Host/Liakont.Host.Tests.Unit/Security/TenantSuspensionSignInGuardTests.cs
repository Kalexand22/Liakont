namespace Liakont.Host.Tests.Unit.Security;

using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.MultiTenancy;
using Liakont.Host.Security;
using Stratum.Common.Abstractions.MultiTenancy;
using Xunit;

/// <summary>
/// Garde de refus au SIGN-IN d'un tenant suspendu (OPS03.4 lot B) — la décision extraite de
/// l'événement OIDC : suspendu → refusé ; super-admin, issuer absent/inconnu, realm hors registre
/// ou tenant actif → jamais refusé.
/// </summary>
public sealed class TenantSuspensionSignInGuardTests
{
    private const string Issuer = "http://localhost:8080/realms/stratum-acme";

    [Fact]
    public async Task A_User_Of_A_Suspended_Tenant_Is_Refused()
    {
        var refused = await TenantSuspensionSignInGuard.ShouldRefuseAsync(
            Principal(issuer: Issuer),
            new FakeRealmRegistry("stratum-acme", "acme"),
            new FakeLookup(suspended: true),
            CancellationToken.None);

        refused.Should().BeTrue();
    }

    [Fact]
    public async Task A_Super_Admin_Is_Never_Refused()
    {
        var refused = await TenantSuspensionSignInGuard.ShouldRefuseAsync(
            Principal(issuer: Issuer, roles: ["SystemAdmin"]),
            new FakeRealmRegistry("stratum-acme", "acme"),
            new FakeLookup(suspended: true),
            CancellationToken.None);

        refused.Should().BeFalse("l'opérateur d'instance doit pouvoir se connecter pour réactiver");
    }

    [Fact]
    public async Task An_Active_Tenant_Is_Not_Refused()
    {
        var refused = await TenantSuspensionSignInGuard.ShouldRefuseAsync(
            Principal(issuer: Issuer),
            new FakeRealmRegistry("stratum-acme", "acme"),
            new FakeLookup(suspended: false),
            CancellationToken.None);

        refused.Should().BeFalse();
    }

    [Fact]
    public async Task A_Principal_Without_Issuer_Is_Not_Refused()
    {
        var refused = await TenantSuspensionSignInGuard.ShouldRefuseAsync(
            Principal(issuer: null),
            new FakeRealmRegistry("stratum-acme", "acme"),
            new FakeLookup(suspended: true),
            CancellationToken.None);

        refused.Should().BeFalse("ce garde ne décide que de la suspension, pas de la validité du jeton");
    }

    [Fact]
    public async Task A_Realm_Unknown_To_The_Registry_Is_Not_Refused()
    {
        var refused = await TenantSuspensionSignInGuard.ShouldRefuseAsync(
            Principal(issuer: "http://localhost:8080/realms/autre-realm"),
            new FakeRealmRegistry("stratum-acme", "acme"),
            new FakeLookup(suspended: true),
            CancellationToken.None);

        refused.Should().BeFalse();
    }

    private static ClaimsPrincipal Principal(string? issuer, string[]? roles = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "user-1") };
        if (issuer is not null)
        {
            claims.Add(new Claim("iss", issuer));
        }

        foreach (var role in roles ?? [])
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private sealed class FakeRealmRegistry : IRealmRegistry
    {
        private readonly string _realm;
        private readonly string _tenantId;

        public FakeRealmRegistry(string realm, string tenantId)
        {
            _realm = realm;
            _tenantId = tenantId;
        }

        public bool IsKnownIssuer(string issuer) => true;

        public void RegisterRealm(string realmName, string tenantId, string authority)
        {
        }

        public void UnregisterRealm(string realmName, string authority)
        {
        }

        public bool TryGetTenantId(string realmName, out string? tenantId)
        {
            tenantId = realmName == _realm ? _tenantId : null;
            return tenantId is not null;
        }
    }

    private sealed class FakeLookup : ITenantSuspensionLookup
    {
        private readonly bool _suspended;

        public FakeLookup(bool suspended) => _suspended = suspended;

        public Task<bool> IsSuspendedAsync(string tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_suspended);

        public void Invalidate(string tenantId)
        {
        }
    }
}
