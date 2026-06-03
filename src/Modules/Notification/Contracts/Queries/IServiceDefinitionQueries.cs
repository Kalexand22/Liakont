namespace Stratum.Modules.Notification.Contracts.Queries;

using Stratum.Modules.Notification.Contracts.DTOs;

public interface IServiceDefinitionQueries
{
    Task<IReadOnlyList<ServiceDefinitionDto>> List(Guid? companyId, CancellationToken ct = default);

    Task<ServiceDefinitionDto?> GetByCode(string code, Guid? companyId, CancellationToken ct = default);

    Task<ServiceDefinitionDto?> GetById(Guid id, CancellationToken ct = default);
}
