namespace Liakont.Host.Tests.Unit.Components;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Liakont.Host.Components.Layout;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.MultiTenancy;
using Xunit;

/// <summary>
/// Tests bUnit du badge « tenant courant » du chrome de la console (FIX04a). Vérifie la résolution
/// du DisplayName depuis le registre des tenants (lecture tenant-scopée) et la dégradation gracieuse :
/// aucun badge quand aucun tenant n'est résolu ou quand la lecture échoue (le chrome ne casse jamais).
/// </summary>
public sealed class TenantBadgeTests : BunitContext
{
    private readonly BunitAuthorizationContext _authContext;

    public TenantBadgeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();

        // RB1 — TenantBadge lit l'état d'auth (le badge est masqué pour un super-admin cross-tenant).
        // Défaut : utilisateur NON authentifié (donc non super-admin) → comportement nominal du badge
        // préservé pour les cas existants.
        _authContext = this.AddAuthorization();
    }

    [Fact]
    public void Shows_the_resolved_tenant_display_name()
    {
        Services.AddScoped<ITenantContext>(_ => new FakeTenantContext("acme"));
        Services.AddScoped<ITenantQueries>(_ => FakeTenantQueries.With("acme", "Cabinet Durand"));

        var cut = Render<TenantBadge>();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='topbar-tenant']").TextContent.Should().Contain("Cabinet Durand"));
    }

    [Fact]
    public void Renders_nothing_when_no_tenant_is_resolved()
    {
        Services.AddScoped<ITenantContext>(_ => new FakeTenantContext(null));
        Services.AddScoped<ITenantQueries>(_ => FakeTenantQueries.With("acme", "Cabinet Durand"));

        var cut = Render<TenantBadge>();

        cut.FindAll("[data-testid='topbar-tenant']").Should().BeEmpty();
    }

    [Fact]
    public void Renders_nothing_when_the_tenant_lookup_fails()
    {
        Services.AddScoped<ITenantContext>(_ => new FakeTenantContext("acme"));
        Services.AddScoped<ITenantQueries>(_ => FakeTenantQueries.Throwing());

        var cut = Render<TenantBadge>();

        // Lecture annexe : un échec ne doit jamais casser le chrome — pas de badge, pas d'exception propagée.
        cut.FindAll("[data-testid='topbar-tenant']").Should().BeEmpty();
    }

    [Fact]
    public void Renders_nothing_for_a_cross_tenant_super_admin()
    {
        // RB1 : un super-admin (stratum-admin) opère en cross-tenant — aucun « tenant courant » affiché,
        // même si un tenant est résolu par défaut (via le RealmTenantMap).
        _authContext.SetAuthorized("sysadmin");
        _authContext.SetRoles("stratum-admin");
        Services.AddScoped<ITenantContext>(_ => new FakeTenantContext("acme"));
        Services.AddScoped<ITenantQueries>(_ => FakeTenantQueries.With("acme", "Cabinet Durand"));

        var cut = Render<TenantBadge>();

        cut.FindAll("[data-testid='topbar-tenant']").Should().BeEmpty();
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public FakeTenantContext(string? tenantId) => TenantId = tenantId;

        public string? TenantId { get; }

        public bool IsResolved => TenantId is not null;
    }

    private sealed class FakeTenantQueries : ITenantQueries
    {
        private readonly bool _throw;
        private readonly Dictionary<string, string> _byId;

        private FakeTenantQueries(bool shouldThrow, Dictionary<string, string> byId)
        {
            _throw = shouldThrow;
            _byId = byId;
        }

        public static FakeTenantQueries With(string id, string displayName) =>
            new(false, new Dictionary<string, string>(StringComparer.Ordinal) { [id] = displayName });

        public static FakeTenantQueries Throwing() => new(true, new Dictionary<string, string>(StringComparer.Ordinal));

        public Task<IReadOnlyList<TenantDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TenantDto>>(Array.Empty<TenantDto>());

        public Task<TenantDto?> GetByIdAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            if (_throw)
            {
                throw new InvalidOperationException("Échec simulé de la lecture du tenant courant.");
            }

            if (!_byId.TryGetValue(tenantId, out var displayName))
            {
                return Task.FromResult<TenantDto?>(null);
            }

            return Task.FromResult<TenantDto?>(new TenantDto
            {
                Id = tenantId,
                DisplayName = displayName,
                AdminEmail = "admin@example.test",
                DatabaseName = "db-acme",
                IsActive = true,
                ProvisionedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            });
        }
    }
}
