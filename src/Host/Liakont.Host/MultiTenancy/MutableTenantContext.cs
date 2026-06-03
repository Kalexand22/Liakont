namespace Liakont.Host.MultiTenancy;

using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Mutable implementation of <see cref="ITenantContext"/>.
/// Set by the tenant middleware, read by consumers through the <see cref="ITenantContext"/> interface.
/// Lives in Host to prevent domain/application layers from mutating the tenant context.
/// </summary>
internal sealed class MutableTenantContext : ITenantContext
{
    public string? TenantId { get; set; }

    public bool IsResolved => TenantId is not null;
}
