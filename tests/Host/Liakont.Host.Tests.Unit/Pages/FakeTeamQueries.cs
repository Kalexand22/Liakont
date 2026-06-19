namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Modules.Identity.Contracts.DTOs;
using Stratum.Modules.Identity.Contracts.Queries;

/// <summary>Double de <see cref="ITeamQueries"/> renvoyant des jeux fixés (tests bUnit des pages Identity, RB6 P2).</summary>
internal sealed class FakeTeamQueries : ITeamQueries
{
    private readonly IReadOnlyList<TeamDto> _teams;
    private readonly TeamDto? _team;
    private readonly IReadOnlyList<TeamMemberDto> _members;

    public FakeTeamQueries(
        IReadOnlyList<TeamDto>? teams = null,
        TeamDto? team = null,
        IReadOnlyList<TeamMemberDto>? members = null)
    {
        _teams = teams ?? [];
        _team = team;
        _members = members ?? [];
    }

    public Task<IReadOnlyList<TeamDto>> List(CancellationToken ct = default) =>
        Task.FromResult(_teams);

    public Task<TeamDto?> GetById(Guid teamId, CancellationToken ct = default) =>
        Task.FromResult(_team);

    public Task<IReadOnlyList<TeamMemberDto>> GetMembers(Guid teamId, CancellationToken ct = default) =>
        Task.FromResult(_members);
}
