namespace Liakont.Modules.Ingestion.Tests.Integration.Doubles;

using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>Contexte tenant de test : slug fixe (isolation par test).</summary>
internal sealed class TestTenantContext : ITenantContext
{
    public TestTenantContext(string? tenantId)
    {
        TenantId = tenantId;
    }

    public string? TenantId { get; }

    public bool IsResolved => TenantId is not null;
}
