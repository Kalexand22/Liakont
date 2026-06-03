namespace Stratum.Modules.Identity.Contracts.Queries;

using Stratum.Modules.Identity.Contracts.DTOs;

public interface ITeamQueries
{
    Task<IReadOnlyList<TeamDto>> List(CancellationToken ct = default);

    Task<TeamDto?> GetById(Guid teamId, CancellationToken ct = default);

    Task<IReadOnlyList<TeamMemberDto>> GetMembers(Guid teamId, CancellationToken ct = default);
}
