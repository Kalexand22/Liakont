namespace Stratum.Modules.Identity.Contracts.Queries;

using Stratum.Modules.Identity.Contracts.DTOs;

public interface IAgentQueries
{
    Task<IReadOnlyList<AgentDto>> List(CancellationToken ct = default);

    Task<AgentDto?> GetById(Guid agentProfileId, CancellationToken ct = default);

    Task<AgentDto?> GetByUserId(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<AgentCompetenceDto>> GetCompetences(Guid userId, CancellationToken ct = default);
}
