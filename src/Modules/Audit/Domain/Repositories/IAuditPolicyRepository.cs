namespace Stratum.Modules.Audit.Domain.Repositories;

using Stratum.Modules.Audit.Domain.Entities;

public interface IAuditPolicyRepository
{
    Task<AuditPolicy?> GetByEntityType(string entityType, CancellationToken ct = default);

    Task Insert(AuditPolicy policy, CancellationToken ct = default);

    Task Update(AuditPolicy policy, CancellationToken ct = default);
}
