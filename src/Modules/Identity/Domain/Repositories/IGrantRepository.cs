namespace Stratum.Modules.Identity.Domain.Repositories;

using Stratum.Modules.Identity.Domain.Entities;

public interface IGrantRepository
{
    Task<IReadOnlyList<Grant>> GetByRoleId(Guid roleId, CancellationToken ct = default);

    Task Insert(Grant grant, CancellationToken ct = default);

    Task Delete(Guid roleId, string permission, CancellationToken ct = default);
}
