namespace Stratum.Modules.Party.Contracts.Queries;

using Stratum.Modules.Party.Contracts.DTOs;

public interface IExternalIdQueries
{
    Task<ExternalIdDto?> GetByExternalId(string systemCode, string externalId, CancellationToken ct = default);

    Task<IReadOnlyList<ExternalIdDto>> GetExternalIds(Guid partyId, CancellationToken ct = default);
}
