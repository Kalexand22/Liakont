namespace Stratum.Modules.Notification.Contracts.Queries;

using Stratum.Modules.Notification.Contracts.DTOs;

public interface IDeliveryRecordQueries
{
    Task<IReadOnlyList<DeliveryRecordDto>> ListByEntity(string entityType, string entityId, CancellationToken ct = default);

    Task<IReadOnlyList<DeliveryRecordDto>> ListSlaBreaches(Guid? companyId, CancellationToken ct = default);

    Task<IReadOnlyList<DeliveryRecordDto>> ListFailedForRetry(int maxRetryCount, CancellationToken ct = default);
}
