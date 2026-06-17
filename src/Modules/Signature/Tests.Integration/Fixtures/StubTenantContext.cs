namespace Liakont.Modules.Signature.Tests.Integration.Fixtures;

using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>Stub de <see cref="ITenantContext"/> pour les tests d'intégration : renvoie un tenant fixe.</summary>
internal sealed class StubTenantContext : ITenantContext
{
    public StubTenantContext(string tenantId) => TenantId = tenantId;

    public string? TenantId { get; }

    public bool IsResolved => TenantId is not null;
}
