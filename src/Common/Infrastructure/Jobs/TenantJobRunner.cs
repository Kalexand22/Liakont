// Liakont addition (SOL06): multi-tenant job mechanism — not part of the original Stratum vendoring.
namespace Stratum.Common.Infrastructure.Jobs;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Default <see cref="ITenantJobRunner"/>. Lists active tenants from the instance catalog
/// (<see cref="ITenantQueries"/>, system database) and runs the given <see cref="ITenantJob"/> once
/// per tenant inside a tenant-scoped DI scope obtained from <see cref="ITenantScopeFactory"/>, so the
/// job's connection is switched to that tenant's database. Failures are isolated per tenant. An optional
/// per-tenant time budget (<see cref="TenantJobRunnerOptions.PerTenantTimeout"/>) caps a single tenant's
/// execution so a slow tenant becomes an isolated failure instead of blocking the whole run (RDL08).
/// </summary>
public sealed partial class TenantJobRunner : ITenantJobRunner
{
    private readonly ITenantQueries _tenantQueries;
    private readonly ITenantScopeFactory _scopeFactory;
    private readonly ILogger<TenantJobRunner> _logger;
    private readonly TenantJobRunnerOptions _options;

    public TenantJobRunner(
        ITenantQueries tenantQueries,
        ITenantScopeFactory scopeFactory,
        ILogger<TenantJobRunner> logger,
        IOptions<TenantJobRunnerOptions>? options = null)
    {
        // Options are optional: DI always supplies them (AddTenantJobs calls AddOptions), and the per-tenant
        // budget defaults to disabled (null) when absent — so existing 3-arg callers keep their behaviour.
        _tenantQueries = tenantQueries;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options?.Value ?? new TenantJobRunnerOptions();
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
                await ExecuteForTenantAsync(job, context, tenant.Id, cancellationToken).ConfigureAwait(false);
                succeeded++;
                LogTenantSucceeded(_logger, job.Name, tenant.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // CALLER-requested cancellation (shutdown) aborts the whole run; it is not a per-tenant
                // failure. The filter keys on the CALLER token specifically (A6-runtime-4) so a future
                // per-tenant timeout (linked CTS, below) is never mistaken for a caller abort.
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "TenantJob '{JobName}' EXCEEDED the per-tenant time budget of {Budget} for tenant '{TenantId}' (isolated as a failure; other tenants continue)")]
    private static partial void LogTenantBudgetExceeded(ILogger logger, string jobName, string tenantId, TimeSpan budget);

    /// <summary>
    /// Runs the job for a single tenant, applying the optional per-tenant time budget (A6-scale-3). When a
    /// budget is set, a linked <see cref="CancellationTokenSource"/> cancels the job after the timeout; the
    /// resulting cancellation is re-thrown as a <see cref="TimeoutException"/> so it is caught by the caller's
    /// generic handler as an ISOLATED tenant failure — it never reaches the caller-cancellation filter, so it
    /// never aborts the run. A genuine caller cancellation still propagates as <see cref="OperationCanceledException"/>.
    /// </summary>
    private async Task ExecuteForTenantAsync(
        ITenantJob job,
        TenantJobContext context,
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (_options.PerTenantTimeout is not { } budget)
        {
            await job.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var tenantCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        tenantCts.CancelAfter(budget);
        try
        {
            await job.ExecuteAsync(context, tenantCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (tenantCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The PER-TENANT budget fired (not the caller). Convert to a TimeoutException so this tenant is
            // isolated as a failure (caught generically by the caller) while the run continues for the others.
            LogTenantBudgetExceeded(_logger, job.Name, tenantId, budget);
            throw new TimeoutException(
                $"Le traitement du tenant '{tenantId}' par le job '{job.Name}' a dépassé le budget de temps de {budget} et a été interrompu.");
        }
    }
}
