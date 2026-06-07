namespace Liakont.Modules.Supervision.Tests.Integration.Doubles;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>Catalogue de tenants de test : liste fixe de tenants (le runner ne balaie que les actifs).</summary>
internal sealed class ListTenantQueries : ITenantQueries
{
    private readonly IReadOnlyList<TenantDto> _tenants;

    public ListTenantQueries(params TenantDto[] tenants)
    {
        _tenants = tenants;
    }

    public static TenantDto ActiveTenant(string id) => new()
    {
        Id = id,
        DisplayName = id,
        AdminEmail = $"admin@{id}.test",
        DatabaseName = id,
        RealmName = null,
        IsActive = true,
        ProvisionedAt = DateTimeOffset.UnixEpoch,
    };

    public Task<IReadOnlyList<TenantDto>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_tenants);

    public Task<TenantDto?> GetByIdAsync(string tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_tenants.FirstOrDefault(t => t.Id == tenantId));
}
