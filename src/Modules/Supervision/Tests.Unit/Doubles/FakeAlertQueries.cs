namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Contracts;
using Liakont.Modules.Supervision.Contracts.DTOs;

/// <summary>Lectures d'alertes fictives (dashboard SUP02) : alertes actives + historique récent fixés.</summary>
internal sealed class FakeAlertQueries : IAlertQueries
{
    private readonly IReadOnlyList<AlertDto> _active;
    private readonly IReadOnlyList<AlertDto> _recent;

    public FakeAlertQueries(IReadOnlyList<AlertDto>? active = null, IReadOnlyList<AlertDto>? recent = null)
    {
        _active = active ?? [];
        _recent = recent ?? _active;
    }

    public Task<IReadOnlyList<AlertDto>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_active);

    public Task<IReadOnlyList<AlertDto>> ListRecentAsync(int max, CancellationToken cancellationToken = default) =>
        Task.FromResult(_recent);

    public Task<AlertDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_recent.FirstOrDefault(a => a.Id == id));
}
