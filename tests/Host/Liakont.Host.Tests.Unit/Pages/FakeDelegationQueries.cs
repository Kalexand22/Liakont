namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Modules.Identity.Contracts.DTOs;
using Stratum.Modules.Identity.Contracts.Queries;

/// <summary>Double de <see cref="IDelegationQueries"/> renvoyant des jeux fixés (tests bUnit des pages Identity, RB6 P2).</summary>
internal sealed class FakeDelegationQueries : IDelegationQueries
{
    private readonly IReadOnlyList<DelegationDto> _delegations;
    private readonly DelegationDto? _delegation;

    public FakeDelegationQueries(
        IReadOnlyList<DelegationDto>? delegations = null,
        DelegationDto? delegation = null)
    {
        _delegations = delegations ?? [];
        _delegation = delegation;
    }

    public Task<IReadOnlyList<DelegationDto>> List(CancellationToken ct = default) =>
        Task.FromResult(_delegations);

    public Task<DelegationDto?> GetById(Guid delegationId, CancellationToken ct = default) =>
        Task.FromResult(_delegation);

    public Task<IReadOnlyList<DelegationDto>> GetActiveDelegationsForUser(Guid userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DelegationDto>>([]);
}
