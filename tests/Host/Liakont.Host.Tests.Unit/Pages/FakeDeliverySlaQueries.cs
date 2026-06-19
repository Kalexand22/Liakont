namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;

/// <summary>Double de <see cref="IDeliverySlaQueries"/> renvoyant des jeux fixés (tests bUnit Notification, RB6 P2).</summary>
internal sealed class FakeDeliverySlaQueries : IDeliverySlaQueries
{
    private readonly IReadOnlyList<DeliverySlaDto> _slas;

    public FakeDeliverySlaQueries(IReadOnlyList<DeliverySlaDto>? slas = null) => _slas = slas ?? [];

    public Task<IReadOnlyList<DeliverySlaDto>> List(Guid? companyId, CancellationToken ct = default) =>
        Task.FromResult(_slas);

    public Task<DeliverySlaDto?> GetByCategory(string category, Guid? companyId, CancellationToken ct = default) =>
        Task.FromResult(_slas.FirstOrDefault(s => s.Category == category));

    public Task<DeliverySlaDto?> GetById(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_slas.FirstOrDefault(s => s.Id == id) ?? (_slas.Count == 0 ? null : _slas[0]));
}
