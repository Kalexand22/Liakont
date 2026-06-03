namespace Stratum.Common.Infrastructure.Database;

using System.Data;

/// <summary>
/// Opens connections to the system (shared) database.
/// Used by singleton services (OutboxWorker, AuditWriter, etc.) that must not
/// be scoped to a tenant.
/// </summary>
public interface ISystemConnectionFactory
{
    Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default);
}
