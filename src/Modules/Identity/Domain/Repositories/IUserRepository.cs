namespace Stratum.Modules.Identity.Domain.Repositories;

using Stratum.Modules.Identity.Domain.Entities;

public interface IUserRepository
{
    Task<User?> GetById(Guid id, CancellationToken ct = default);

    Task<User?> GetByUsername(string username, CancellationToken ct = default);

    Task<User?> GetByExternalId(string externalId, CancellationToken ct = default);

    Task<User?> GetByEmail(string email, CancellationToken ct = default);

    Task Insert(User user, CancellationToken ct = default);

    Task Update(User user, CancellationToken ct = default);
}
