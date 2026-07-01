namespace Liakont.Modules.Ged.Tests.Integration.Doubles;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Liakont.Modules.Ged.Application;
using Liakont.Modules.Ged.Application.Mapping;
using Liakont.Modules.Ged.Infrastructure;
using Liakont.Modules.Ged.Infrastructure.Index;
using Liakont.Modules.Ged.Infrastructure.Mapping;
using Liakont.Modules.Staging.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Stub de <see cref="ITenantScopeFactory"/> pour tester le consommateur d'ingestion GED (GED05b) hors Host : mappe
/// un slug de tenant vers un fournisseur de services résolvant les surfaces GED (catalogues, UoW d'index, profils)
/// sur la BASE DU TENANT correspondante, plus le magasin de staging PARTAGÉ avec le handler. Les implémentations
/// Postgres sont sans état (chaque appel ouvre sa propre connexion), donc partageables entre scopes concurrents.
/// </summary>
internal sealed class StubTenantScopeFactory : ITenantScopeFactory
{
    private readonly Dictionary<string, IServiceProvider> _providers;

    public StubTenantScopeFactory(IReadOnlyDictionary<string, IConnectionFactory> tenants, IPayloadStagingStore staging)
    {
        var providers = new Dictionary<string, IServiceProvider>(StringComparer.Ordinal);
        foreach (var (tenantId, factory) in tenants)
        {
            var axisCatalog = new PostgresAxisCatalog(factory);
            var entityCatalog = new PostgresEntityCatalog(factory);
            var uowFactory = new PostgresGedIndexUnitOfWorkFactory(factory);
            var profileStore = new GedMappingProfileRepository(factory);

            var services = new Dictionary<Type, object>
            {
                [typeof(IPayloadStagingStore)] = staging,
                [typeof(IAxisCatalog)] = axisCatalog,
                [typeof(IEntityCatalog)] = entityCatalog,
                [typeof(IGedIndexUnitOfWorkFactory)] = uowFactory,
                [typeof(IGedMappingProfileStore)] = profileStore,

                // Foyer d'écriture unique (GED10) : le consommateur le résout désormais du scope tenant.
                [typeof(IGedDocumentIndexer)] = new GedDocumentIndexer(
                    profileStore, axisCatalog, entityCatalog, uowFactory, NullLogger<GedDocumentIndexer>.Instance),
            };
            providers[tenantId] = new DirectServiceProvider(services);
        }

        _providers = providers;
    }

    public ITenantScope Create(string tenantId) => new StubTenantScope(tenantId, _providers[tenantId]);

    private sealed class StubTenantScope : ITenantScope
    {
        public StubTenantScope(string tenantId, IServiceProvider services)
        {
            TenantId = tenantId;
            Services = services;
        }

        public string TenantId { get; }

        public IServiceProvider Services { get; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DirectServiceProvider : IServiceProvider
    {
        private readonly IReadOnlyDictionary<Type, object> _services;

        public DirectServiceProvider(IReadOnlyDictionary<Type, object> services) => _services = services;

        public object? GetService(Type serviceType) => _services.GetValueOrDefault(serviceType);
    }
}
