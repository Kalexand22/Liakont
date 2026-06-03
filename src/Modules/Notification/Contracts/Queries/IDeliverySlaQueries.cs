namespace Stratum.Modules.Notification.Contracts.Queries;

using Stratum.Modules.Notification.Contracts.DTOs;

public interface IDeliverySlaQueries
{
    Task<IReadOnlyList<DeliverySlaDto>> List(Guid? companyId, CancellationToken ct = default);

    Task<DeliverySlaDto?> GetByCategory(string category, Guid? companyId, CancellationToken ct = default);

    Task<DeliverySlaDto?> GetById(Guid id, CancellationToken ct = default);
}
