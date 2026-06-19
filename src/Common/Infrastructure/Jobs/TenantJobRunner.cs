// Liakont addition (SOL06): multi-tenant job mechanism — not part of the original Stratum vendoring.
namespace Stratum.Common.Infrastructure.Jobs;

using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Default <see cref="ITenantJobRunner"/>. Lists active tenants from the instance catalog
/// (<see cref="ITenantQueries"/>, system database) and runs the given <see cref="ITenantJob"/> once
/// per tenant inside a tenant-scoped DI scope obtained from <see cref="ITenantScopeFactory"/>, so the
/// job's connection is switched to that tenant's database. Failures are isolated per tenant.
/// </summary>
public sealed partial class TenantJobRunner : ITenantJobRunner
{
    private readonly ITenantQueries _tenantQueries;
    private readonly ITenantScopeFactory _scopeFactory;
    private readonly ILogger<TenantJobRunner> _logger;

    public TenantJobRunner(
        ITenantQueries tenantQueries,
        ITenantScopeFactory scopeFactory,
        ILogger<TenantJobRunner> logger)
    {
        _tenantQueries = tenantQueries;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<TenantJobRunSummary> RunForAllTenantsAsync(
        ITenantJob job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var allTenants = await _tenantQueries.ListAsync(cancellationToken).ConfigureAwait(false);
        var activeTenants = allTenants.Where(t => t.IsActive).ToList();

        if (activeTenants.Count == 0)
        {
            // 0 tenant actif est INDISTINCT d'une anomalie (catalogue vide par bug de provisioning, mauvaise
            // base) : on le signale en Warning au lieu d'un no-op Information silencieux (RDL07/A6-runtime-3).
            LogNoActiveTenants(_logger, job.Name);
            return new TenantJobRunSummary(job.Name, 0, 0, []);
        }

        LogStarting(_logger, job.Name, activeTenants.Count);

        var failures = new List<TenantJobFailure>();
        var succeeded = 0;

        foreach (var tenant in activeTenants)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var scope = _scopeFactory.Create(tenant.Id);
                var context = new TenantJobContext(tenant.Id, scope.Services);
                await job.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
                succeeded++;
                LogTenantSucceeded(_logger, job.Name, tenant.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Caller-requested cancellation aborts the whole run; it is not a per-tenant failure.
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(new TenantJobFailure(tenant.Id, ex.Message));
                LogTenantFailed(_logger, job.Name, tenant.Id, ex);
            }
        }

        var summary = new TenantJobRunSummary(job.Name, activeTenants.Count, succeeded, failures);

        if (summary.HasFailures)
        {
            // Run partiel/total échoué : signal structuré UNIFORME (RDL07/A6-runtime-1). Le job se termine
            // tout de même (pas de retry/dead-letter — ADR-0006 §4 : l'escalade des cas à enjeu passe par les
            // états document + le dead-man's-switch) ; ce Warning porte les tenants en échec pour qu'un
            // fan-out à moitié réussi ne soit jamais un faux-vert silencieux.
            LogCompletedWithFailures(
                _logger,
                job.Name,
                summary.TotalTenants,
                summary.SucceededCount,
                summary.FailedCount,
                string.Join(", ", failures.Select(f => f.TenantId)));
        }
        else
        {
            LogCompleted(_logger, job.Name, summary.TotalTenants, summary.SucceededCount, summary.FailedCount);
        }

        return summary;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "TenantJob '{JobName}' starting for {ActiveTenantCount} active tenant(s)")]
    private static partial void LogStarting(ILogger logger, string jobName, int activeTenantCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "TenantJob '{JobName}' succeeded for tenant '{TenantId}'")]
    private static partial void LogTenantSucceeded(ILogger logger, string jobName, string tenantId);

    [LoggerMessage(Level = LogLevel.Error, Message = "TenantJob '{JobName}' failed for tenant '{TenantId}' (other tenants continue)")]
    private static partial void LogTenantFailed(ILogger logger, string jobName, string tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "TenantJob '{JobName}' complete: {TotalTenants} tenant(s), {SucceededCount} succeeded, {FailedCount} failed")]
    private static partial void LogCompleted(ILogger logger, string jobName, int totalTenants, int succeededCount, int failedCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TenantJob '{JobName}' found NO active tenant to run for (possible anomaly: empty instance catalog or wrong database)")]
    private static partial void LogNoActiveTenants(ILogger logger, string jobName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TenantJob '{JobName}' completed with FAILURES: {TotalTenants} tenant(s), {SucceededCount} succeeded, {FailedCount} failed (failed tenants: {FailedTenantIds})")]
    private static partial void LogCompletedWithFailures(ILogger logger, string jobName, int totalTenants, int succeededCount, int failedCount, string failedTenantIds);
}
