namespace Liakont.Modules.Signature.Tests.Unit.TestDoubles.OnSite;

using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>Double de <see cref="ITenantContext"/> renvoyant un tenant fixe.</summary>
internal sealed class FakeTenantContext : ITenantContext
{
    public FakeTenantContext(string? tenantId) => TenantId = tenantId;

    public string? TenantId { get; }

    public bool IsResolved => TenantId is not null;
}
