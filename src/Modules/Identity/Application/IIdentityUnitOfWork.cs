namespace Stratum.Modules.Identity.Application;

using Stratum.Common.Abstractions.Events;
using Stratum.Modules.Identity.Domain.Entities;

public interface IIdentityUnitOfWork : IAsyncDisposable
{
    Task InsertUserAsync(User user, CancellationToken ct = default);

    Task UpdateUserAsync(User user, CancellationToken ct = default);

    Task UpdateLastLoginAsync(Guid userId, DateTimeOffset lastLoginAt, CancellationToken ct = default);

    Task InsertRoleAsync(Role role, CancellationToken ct = default);

    Task UpdateRoleAsync(Role role, CancellationToken ct = default);

    Task DeleteRoleAsync(Guid roleId, CancellationToken ct = default);

    Task InsertGrantAsync(Grant grant, CancellationToken ct = default);

    Task DeleteGrantAsync(Guid roleId, string permission, CancellationToken ct = default);

    // Agent profiles
    Task<AgentProfile?> GetAgentProfileByIdAsync(Guid id, CancellationToken ct = default);

    Task<AgentProfile?> GetAgentProfileByUserIdAsync(Guid userId, CancellationToken ct = default);

    Task InsertAgentProfileAsync(AgentProfile profile, CancellationToken ct = default);

    Task UpdateAgentProfileAsync(AgentProfile profile, CancellationToken ct = default);

    // Teams
    Task<Team?> GetTeamByIdAsync(Guid id, CancellationToken ct = default);

    Task InsertTeamAsync(Team team, CancellationToken ct = default);

    Task UpdateTeamAsync(Team team, CancellationToken ct = default);

    Task DeleteTeamAsync(Guid id, CancellationToken ct = default);

    Task InsertTeamMemberAsync(TeamMember member, CancellationToken ct = default);

    Task DeleteTeamMemberAsync(Guid memberId, CancellationToken ct = default);

    // Competences
    Task InsertAgentCompetenceAsync(AgentCompetence competence, CancellationToken ct = default);

    Task DeleteAgentCompetenceAsync(Guid competenceId, CancellationToken ct = default);

    // Delegations
    Task<Delegation?> GetDelegationByIdAsync(Guid id, CancellationToken ct = default);

    Task InsertDelegationAsync(Delegation delegation, CancellationToken ct = default);

    Task UpdateDelegationAsync(Delegation delegation, CancellationToken ct = default);

    Task CommitWithEventAsync<TPayload>(IntegrationEvent<TPayload> integrationEvent, CancellationToken ct = default);

    Task CommitAsync(CancellationToken ct = default);
}

public interface IIdentityUnitOfWorkFactory
{
    Task<IIdentityUnitOfWork> BeginAsync(CancellationToken ct = default);
}
