namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>Registre de tenants fictif (base système) : liste fixe + résolution par id, pour le dashboard SUP02.</summary>
internal sealed class FakeTenantQueries : ITenantQueries
{
    private readonly IReadOnlyList<TenantDto> _tenants;

    public FakeTenantQueries(params TenantDto[] tenants) => _tenants = tenants;

    public Task<IReadOnlyList<TenantDto>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_tenants);

    public Task<TenantDto?> GetByIdAsync(string tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_tenants.FirstOrDefault(t => t.Id == tenantId));
}
