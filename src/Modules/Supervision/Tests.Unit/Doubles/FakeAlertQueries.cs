namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Contracts;
using Liakont.Modules.Supervision.Contracts.DTOs;

/// <summary>
/// Lectures d'alertes factices, partagées par les tests de supervision :
/// - SUP03 (digest / notifications) : ctor varargs des alertes actives (source du digest) ; l'historique
///   récent reprend les actives (ces lectures ne sont pas sollicitées par SUP03).
/// - SUP02 (dashboard cross-tenant) : alertes actives ET historique récent distincts ; <see cref="GetByIdAsync"/>
///   cherche dans l'historique récent.
/// </summary>
internal sealed class FakeAlertQueries : IAlertQueries
{
    private readonly IReadOnlyList<AlertDto> _active;
    private readonly IReadOnlyList<AlertDto> _recent;

    public FakeAlertQueries(params AlertDto[] active)
    {
        _active = active;
        _recent = active;
    }

    public FakeAlertQueries(IReadOnlyList<AlertDto> active, IReadOnlyList<AlertDto>? recent)
    {
        _active = active;
        _recent = recent ?? active;
    }

    public Task<IReadOnlyList<AlertDto>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_active);

    public Task<IReadOnlyList<AlertDto>> ListRecentAsync(int max, CancellationToken cancellationToken = default) =>
        Task.FromResult(_recent);

    public Task<AlertDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_recent.FirstOrDefault(a => a.Id == id));
}
