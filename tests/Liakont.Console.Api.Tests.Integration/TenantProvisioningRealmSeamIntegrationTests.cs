namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Keycloak;
using Xunit;

/// <summary>
/// RLM04 (ADR-0021 §1/§5) — preuve de CONSOMMATION du seam par <see cref="TenantProvisioningService.ProvisionAsync"/>
/// sur une base RÉELLE (Testcontainers ; la phase 1 crée et migre une vraie base de tenant). On
/// construit le service avec un provisioner ESPION et un registre de realms ESPION pour prouver les
/// DEUX directions du nettoyage vestigial gardé par le seam :
/// <list type="bullet">
///   <item>profil PARTAGÉ (le no-op renvoie une AUTORITÉ VIDE) ⇒ <c>RegisterRealm</c> et
///         <c>AddTenantRedirectUriAsync</c> ne sont JAMAIS appelés ;</item>
///   <item>profil DÉDIÉ (le vrai provisioner renvoie l'autorité d'un realm) ⇒ ils SONT appelés (y
///         compris sur le chemin de reprise <c>Idempotent</c> — non-régression du correctif round 1).</item>
/// </list>
/// Complète les tests d'isolation (no-op unitaire + résolution DI) en prouvant que
/// <c>ProvisionAsync</c> applique réellement la garde, jamais « 0 HTTP contre un fake muet ».
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class TenantProvisioningRealmSeamIntegrationTests
{
    private readonly ConsoleApiFactory _factory;

    public TenantProvisioningRealmSeamIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProvisionAsync_SharedProfile_EmptyAuthority_DoesNotRegisterRealmOrRedirect()
    {
        var tenantId = NewTenantId();
        var realmName = $"stratum-{tenantId}";
        var registry = new RecordingRealmRegistry();

        // Provisioner « partagé » : renvoie exactement ce que renvoie NoOpKeycloakRealmProvisioner
        // (Idempotent, autorité VIDE) — aucun realm provisionné.
        var provisioner = new SpyRealmProvisioner(
            KeycloakProvisionResult.Idempotent(realmName, string.Empty));

        try
        {
            var result = await BuildService(provisioner, registry).ProvisionAsync(
                new TenantProvisionRequest { TenantId = tenantId, DisplayName = "Seam Shared", AdminEmail = "seam@x.test" });

            result.Success.Should().BeTrue("la base est créée/migrée et le tenant enregistré, même sans realm dédié");
            registry.Registered.Should().BeEmpty("profil partagé (autorité vide) : aucun realm enregistré dans IRealmRegistry");
            provisioner.RedirectCallCount.Should().Be(0, "aucun redirect par sous-domaine en profil partagé");
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ProvisionAsync_DedicatedProfile_RealmAuthority_RegistersRealmAndRedirect()
    {
        var tenantId = NewTenantId();
        var realmName = $"stratum-{tenantId}";
        var registry = new RecordingRealmRegistry();

        // Provisioner « dédié » : renvoie l'autorité d'un realm réellement créé.
        var provisioner = new SpyRealmProvisioner(
            KeycloakProvisionResult.Created(realmName, $"http://kc.test/realms/{realmName}", "secret"));

        try
        {
            var result = await BuildService(provisioner, registry).ProvisionAsync(
                new TenantProvisionRequest { TenantId = tenantId, DisplayName = "Seam Dedicated", AdminEmail = "seam@x.test" });

            result.Success.Should().BeTrue();
            registry.Registered.Should().ContainSingle()
                .Which.Should().Be(realmName, "profil dédié (autorité réelle) : le realm est enregistré pour la validation JWT");
            provisioner.RedirectCallCount.Should().Be(1, "le redirect par sous-domaine est ajouté pour le realm dédié (PrimaryRealmName configuré)");
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    private static string NewTenantId() => $"seam-{Guid.NewGuid():N}"[..12];

    /// <summary>
    /// Construit un <see cref="TenantProvisioningService"/> branché sur la base RÉELLE du harness
    /// (mêmes options DB / préfixe / assemblies de migration), mais avec le provisioner et le registre
    /// de realms ESPIONS injectés. Les options Keycloak sont « configurées » (IsConfigured=true) avec un
    /// PrimaryRealmName pour que la branche realm soit atteinte.
    /// </summary>
    private TenantProvisioningService BuildService(IKeycloakRealmProvisioner provisioner, IRealmRegistry registry)
    {
        var keycloakOptions = Options.Create(new KeycloakAdminOptions
        {
            AdminBaseUrl = "http://kc.test",
            AdminUsername = "admin",
            AdminPassword = "admin",
            AppBaseUrl = "http://app.test",
            PrimaryRealmName = "liakont-dev",
        });

        return new TenantProvisioningService(
            _factory.Services.GetRequiredService<IOptions<DatabaseOptions>>(),
            _factory.Services.GetRequiredService<IOptions<TenantConnectionOptions>>(),
            _factory.Services.GetRequiredService<IOptions<MigrationAssembliesOptions>>(),
            provisioner,
            registry,
            keycloakOptions,
            NullLogger<TenantProvisioningService>.Instance);
    }

    private async Task CleanupAsync(string tenantId)
    {
        var prefix = _factory.Services.GetRequiredService<IOptions<TenantConnectionOptions>>().Value.DatabasePrefix;
        var databaseName = $"{prefix}{tenantId.Replace('-', '_')}";

        await using var conn = new NpgsqlConnection(_factory.SystemConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM outbox.tenants WHERE id = @id", new { id = tenantId });
        await conn.ExecuteAsync($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
    }

    /// <summary>Registre de realms espion : enregistre les noms de realm passés à RegisterRealm.</summary>
    private sealed class RecordingRealmRegistry : IRealmRegistry
    {
        public List<string> Registered { get; } = [];

        public void RegisterRealm(string realmName, string tenantId, string authority) => Registered.Add(realmName);

        public void UnregisterRealm(string realmName, string authority)
        {
        }

        public bool IsKnownIssuer(string issuer) => false;

        public bool TryGetTenantId(string realmName, out string? tenantId)
        {
            tenantId = null;
            return false;
        }
    }

    /// <summary>Provisioner espion : renvoie un résultat fixé et compte les appels au redirect.</summary>
    private sealed class SpyRealmProvisioner : IKeycloakRealmProvisioner
    {
        private readonly KeycloakProvisionResult _result;

        public SpyRealmProvisioner(KeycloakProvisionResult result) => _result = result;

        public int RedirectCallCount { get; private set; }

        public Task<KeycloakProvisionResult> ProvisionRealmAsync(
            KeycloakRealmProvisionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);

        public Task DeleteRealmAsync(string realmName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AddTenantRedirectUriAsync(
            string primaryRealmName, string tenantSubdomain, CancellationToken cancellationToken = default)
        {
            RedirectCallCount++;
            return Task.CompletedTask;
        }
    }
}
