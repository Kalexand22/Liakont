namespace Stratum.Modules.Notification.Contracts.Queries;

using Stratum.Modules.Notification.Contracts.DTOs;

public interface IIntegrationConfigQueries
{
    Task<IntegrationConfigDto?> GetByType(string integrationType, Guid companyId, CancellationToken ct = default);

    Task<IReadOnlyList<IntegrationConfigDto>> ListByCompany(Guid companyId, CancellationToken ct = default);
}
