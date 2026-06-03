namespace Stratum.Modules.Notification.Contracts.Queries;

using Stratum.Modules.Notification.Contracts.DTOs;

public interface IRoutingRuleQueries
{
    Task<IReadOnlyList<RoutingRuleDto>> List(Guid? companyId = null, CancellationToken ct = default);

    Task<IReadOnlyList<RoutingRuleDto>> ListByEntityType(string entityType, Guid? companyId, CancellationToken ct = default);

    Task<RoutingRuleDto?> GetByCode(string code, string entityType, CancellationToken ct = default);

    Task<RoutingRuleDto?> GetById(Guid id, CancellationToken ct = default);
}
