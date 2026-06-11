namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Fabrique de scope tenant fictive (dashboard SUP02) : associe chaque tenant à un fournisseur de services
/// préconfiguré. Un tenant déclaré « en échec » fait LEVER <see cref="Create"/> — pour vérifier la résilience
/// de l'agrégation (un tenant injoignable est signalé <c>ReadFailed</c> et reste visible, jamais masqué).
/// </summary>
internal sealed class FakeTenantScopeFactory : ITenantScopeFactory
{
    private readonly IReadOnlyDictionary<string, IServiceProvider> _byTenant;
    private readonly ISet<string> _failing;

    public FakeTenantScopeFactory(IReadOnlyDictionary<string, IServiceProvider> byTenant, ISet<string>? failing = null)
    {
        _byTenant = byTenant;
        _failing = failing ?? new HashSet<string>();
    }

    public ITenantScope Create(string tenantId)
    {
        if (_failing.Contains(tenantId))
        {
            throw new InvalidOperationException($"Base du tenant {tenantId} injoignable (simulé).");
        }

        return new FakeTenantScope(tenantId, _byTenant[tenantId]);
    }

    private sealed class FakeTenantScope : ITenantScope
    {
        public FakeTenantScope(string tenantId, IServiceProvider services)
        {
            TenantId = tenantId;
            Services = services;
        }

        public string TenantId { get; }

        public IServiceProvider Services { get; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
