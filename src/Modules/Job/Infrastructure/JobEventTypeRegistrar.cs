namespace Stratum.Modules.Job.Infrastructure;

using Microsoft.Extensions.Hosting;
using Stratum.Common.Infrastructure.Outbox;
using Stratum.Modules.Job.Contracts.Events;

internal sealed class JobEventTypeRegistrar : IHostedService
{
    private readonly IEventTypeRegistry _registry;

    public JobEventTypeRegistrar(IEventTypeRegistry registry)
    {
        _registry = registry;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _registry
            .Register<JobCompletedV1>("job.job.completed")
            .Register<JobFailedV1>("job.job.failed")
            .Register<JobDeadLetteredV1>("job.job.dead_lettered");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
