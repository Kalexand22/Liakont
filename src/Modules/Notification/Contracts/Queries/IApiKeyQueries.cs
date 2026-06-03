namespace Stratum.Modules.Notification.Contracts.Queries;

using Stratum.Modules.Notification.Contracts.DTOs;

public interface IApiKeyQueries
{
    Task<IReadOnlyList<ApiKeyDto>> ListByCompany(Guid companyId, CancellationToken ct = default);

    Task<ApiKeyDto?> GetById(Guid apiKeyId, CancellationToken ct = default);
}
