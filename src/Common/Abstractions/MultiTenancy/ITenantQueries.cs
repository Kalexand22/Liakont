namespace Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Read-side queries for tenant administration.
/// </summary>
public interface ITenantQueries
{
    Task<IReadOnlyList<TenantDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<TenantDto?> GetByIdAsync(string tenantId, CancellationToken cancellationToken = default);
}
