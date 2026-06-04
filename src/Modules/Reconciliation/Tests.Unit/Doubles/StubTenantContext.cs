namespace Liakont.Modules.Reconciliation.Tests.Unit.Doubles;

using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>Contexte tenant figé pour les tests unitaires.</summary>
internal sealed class StubTenantContext : ITenantContext
{
    public StubTenantContext(string? tenantId) => TenantId = tenantId;

    public string? TenantId { get; }

    public bool IsResolved => !string.IsNullOrWhiteSpace(TenantId);
}
