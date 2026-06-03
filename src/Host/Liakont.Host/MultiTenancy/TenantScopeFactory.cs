namespace Liakont.Host.MultiTenancy;

using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Composition-root implementation of <see cref="ITenantScopeFactory"/>. Creates a DI scope and
/// establishes the requested tenant as the ambient <see cref="ITenantContext"/> by setting the
/// scoped <see cref="MutableTenantContext"/> — the same mechanism <see cref="TenantMiddleware"/>
/// and <see cref="TenantCircuitHandler"/> use for HTTP requests and Blazor circuits.
/// </summary>
/// <remarks>
/// Lives in Host because <see cref="MutableTenantContext"/> is intentionally kept internal here so
/// that domain/application layers cannot mutate the tenant context. Background multi-tenant fan-out
/// (<see cref="Stratum.Common.Abstractions.Jobs.ITenantJobRunner"/>) is the only other sanctioned
/// caller, and it reaches this seam through the <see cref="ITenantScopeFactory"/> abstraction.
/// </remarks>
internal sealed class TenantScopeFactory : ITenantScopeFactory
{
    private readonly IServiceScopeFactory _scopeFactory;

    public TenantScopeFactory(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public ITenantScope Create(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var scope = _scopeFactory.CreateAsyncScope();
        try
        {
            var tenantContext = scope.ServiceProvider.GetRequiredService<MutableTenantContext>();
            tenantContext.TenantId = tenantId;
            return new TenantScope(scope, tenantId);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private sealed class TenantScope : ITenantScope
    {
        private readonly AsyncServiceScope _scope;

        public TenantScope(AsyncServiceScope scope, string tenantId)
        {
            _scope = scope;
            TenantId = tenantId;
        }

        public string TenantId { get; }

        public IServiceProvider Services => _scope.ServiceProvider;

        public ValueTask DisposeAsync() => _scope.DisposeAsync();
    }
}
