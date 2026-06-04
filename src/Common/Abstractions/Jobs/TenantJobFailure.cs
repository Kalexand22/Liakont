// Liakont addition (SOL06): multi-tenant job mechanism — not part of the original Stratum vendoring.
namespace Stratum.Common.Abstractions.Jobs;

/// <summary>
/// Records that an <see cref="ITenantJob"/> threw for a given tenant. The remaining tenants are
/// processed regardless (failure isolation); each failure is reported here with its tenant id.
/// </summary>
public sealed record TenantJobFailure(string TenantId, string ErrorMessage);
