namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Modules.Identity.Contracts.DTOs;
using Stratum.Modules.Identity.Contracts.Queries;

/// <summary>Double de <see cref="IAgentQueries"/> renvoyant des jeux fixés (tests bUnit des pages Identity, RB6 P2).</summary>
internal sealed class FakeAgentQueries : IAgentQueries
{
    private readonly IReadOnlyList<AgentDto> _agents;
    private readonly AgentDto? _agent;
    private readonly IReadOnlyList<AgentCompetenceDto> _competences;

    public FakeAgentQueries(
        IReadOnlyList<AgentDto>? agents = null,
        AgentDto? agent = null,
        IReadOnlyList<AgentCompetenceDto>? competences = null)
    {
        _agents = agents ?? [];
        _agent = agent;
        _competences = competences ?? [];
    }

    public Task<IReadOnlyList<AgentDto>> List(CancellationToken ct = default) =>
        Task.FromResult(_agents);

    public Task<AgentDto?> GetById(Guid agentProfileId, CancellationToken ct = default) =>
        Task.FromResult(_agent);

    public Task<AgentDto?> GetByUserId(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(_agent);

    public Task<IReadOnlyList<AgentCompetenceDto>> GetCompetences(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(_competences);
}
