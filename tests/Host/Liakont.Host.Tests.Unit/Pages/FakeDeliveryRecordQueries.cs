namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;

/// <summary>Double de <see cref="IDeliveryRecordQueries"/> renvoyant des jeux fixés (tests bUnit Notification, RB6 P2).</summary>
internal sealed class FakeDeliveryRecordQueries : IDeliveryRecordQueries
{
    private readonly IReadOnlyList<DeliveryRecordDto> _breaches;

    public FakeDeliveryRecordQueries(IReadOnlyList<DeliveryRecordDto>? breaches = null) => _breaches = breaches ?? [];

    public Task<IReadOnlyList<DeliveryRecordDto>> ListByEntity(string entityType, string entityId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DeliveryRecordDto>>([]);

    public Task<IReadOnlyList<DeliveryRecordDto>> ListSlaBreaches(Guid? companyId, CancellationToken ct = default) =>
        Task.FromResult(_breaches);

    public Task<IReadOnlyList<DeliveryRecordDto>> ListFailedForRetry(int maxRetryCount, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DeliveryRecordDto>>([]);
}
