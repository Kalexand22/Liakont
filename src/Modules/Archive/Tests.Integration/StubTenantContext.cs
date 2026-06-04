namespace Liakont.Modules.Archive.Tests.Integration;

using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>Contexte tenant figé pour les tests d'intégration.</summary>
internal sealed class StubTenantContext : ITenantContext
{
    public StubTenantContext(string tenantId)
    {
        TenantId = tenantId;
    }

    public string? TenantId { get; }

    public bool IsResolved => !string.IsNullOrWhiteSpace(TenantId);
}
