namespace Stratum.Common.Infrastructure.HealthChecks;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Stratum.Common.Infrastructure.Outbox;

/// <summary>
/// Reports the status of the <see cref="OutboxWorker"/> background service.
/// Degraded when the worker is absent or not yet started; Unhealthy when it has faulted.
/// </summary>
internal sealed class OutboxWorkerHealthCheck : IHealthCheck
{
    private readonly IEnumerable<IHostedService> _hostedServices;

    public OutboxWorkerHealthCheck(IEnumerable<IHostedService> hostedServices)
    {
        _hostedServices = hostedServices;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var worker = _hostedServices.OfType<OutboxWorker>().FirstOrDefault();

        if (worker is null)
        {
            return Task.FromResult(HealthCheckResult.Degraded("Outbox worker is not registered."));
        }

        var executeTask = worker.ExecuteTask;

        if (executeTask is null)
        {
            return Task.FromResult(HealthCheckResult.Degraded("Outbox worker has not started."));
        }

        if (executeTask.IsFaulted)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Outbox worker has faulted.", executeTask.Exception));
        }

        if (executeTask.IsCompleted)
        {
            return Task.FromResult(HealthCheckResult.Degraded("Outbox worker has stopped."));
        }

        return Task.FromResult(HealthCheckResult.Healthy());
    }
}
