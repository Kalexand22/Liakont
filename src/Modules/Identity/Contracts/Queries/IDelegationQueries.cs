namespace Stratum.Modules.Identity.Contracts.Queries;

using Stratum.Modules.Identity.Contracts.DTOs;

public interface IDelegationQueries
{
    Task<IReadOnlyList<DelegationDto>> List(CancellationToken ct = default);

    Task<DelegationDto?> GetById(Guid delegationId, CancellationToken ct = default);

    Task<IReadOnlyList<DelegationDto>> GetActiveDelegationsForUser(Guid userId, CancellationToken ct = default);
}
