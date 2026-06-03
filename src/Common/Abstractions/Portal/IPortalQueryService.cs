namespace Stratum.Common.Abstractions.Portal;

using Stratum.Common.Abstractions.Queries;

/// <summary>
/// Reads public data across all tenant databases for the portal.
/// Operates outside tenant context — no <c>ITenantContext</c> dependency.
/// </summary>
public interface IPortalQueryService
{
    Task<ListResult<PublicEventDto>> GetPublicEventsAsync(
        PortalFilter filter,
        int page,
        int pageSize,
        CancellationToken ct);
}
