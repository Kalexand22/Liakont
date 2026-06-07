namespace Liakont.Modules.Supervision.Tests.Integration.Doubles;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Application;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Fabrique de scope tenant de test : associe chaque tenant à un moteur d'évaluation pré-construit
/// (au-dessus de la base de ce tenant). Reproduit le rôle du <c>TenantScopeFactory</c> du Host — la seule
/// couche autorisée à muter le contexte de tenant — pour exercer le VRAI <c>TenantJobRunner</c> (SOL06).
/// </summary>
internal sealed class MapTenantScopeFactory : ITenantScopeFactory
{
    private readonly IReadOnlyDictionary<string, IAlertEvaluationService> _enginesByTenant;

    public MapTenantScopeFactory(IReadOnlyDictionary<string, IAlertEvaluationService> enginesByTenant)
    {
        _enginesByTenant = enginesByTenant;
    }

    public ITenantScope Create(string tenantId)
    {
        if (!_enginesByTenant.TryGetValue(tenantId, out var engine))
        {
            throw new InvalidOperationException($"Aucun moteur de test enregistré pour le tenant « {tenantId} ».");
        }

        var services = new SingleServiceProvider(typeof(IAlertEvaluationService), engine);
        return new MapTenantScope(tenantId, services);
    }

    private sealed class MapTenantScope : ITenantScope
    {
        public MapTenantScope(string tenantId, IServiceProvider services)
        {
            TenantId = tenantId;
            Services = services;
        }

        public string TenantId { get; }

        public IServiceProvider Services { get; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
