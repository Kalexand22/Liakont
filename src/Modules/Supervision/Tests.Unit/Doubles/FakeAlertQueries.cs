namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Contracts;
using Liakont.Modules.Supervision.Contracts.DTOs;

/// <summary>
/// Lectures d'alertes factices : <see cref="ListActiveAsync"/> renvoie la liste configurée (source du
/// digest). Les autres lectures lèvent (non sollicitées par SUP03).
/// </summary>
internal sealed class FakeAlertQueries : IAlertQueries
{
    private readonly IReadOnlyList<AlertDto> _active;

    public FakeAlertQueries(params AlertDto[] active)
    {
        _active = active;
    }

    public Task<IReadOnlyList<AlertDto>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_active);

    public Task<IReadOnlyList<AlertDto>> ListRecentAsync(int max, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<AlertDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
