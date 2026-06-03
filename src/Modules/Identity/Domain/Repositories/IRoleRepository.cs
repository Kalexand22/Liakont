namespace Stratum.Modules.Identity.Domain.Repositories;

using Stratum.Modules.Identity.Domain.Entities;

public interface IRoleRepository
{
    Task<Role?> GetById(Guid id, CancellationToken ct = default);

    Task<Role?> GetByName(string name, CancellationToken ct = default);

    Task<IReadOnlyList<Role>> GetAll(CancellationToken ct = default);

    Task Insert(Role role, CancellationToken ct = default);
}
