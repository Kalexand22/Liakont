namespace Stratum.Modules.Job.Infrastructure;

using System.Text.Json;
using Stratum.Modules.Job.Application;
using Stratum.Modules.Job.Contracts;
using Stratum.Modules.Job.Domain.Entities;

internal sealed class PostgresJobQueue : IJobQueue
{
    private readonly IJobUnitOfWorkFactory _uowFactory;

    public PostgresJobQueue(IJobUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;
    }

    public async Task<Guid> EnqueueAsync<T>(
        T payload,
        int priority = 0,
        DateTimeOffset? scheduledAt = null,
        Guid? companyId = null,
        CancellationToken ct = default)
    {
        var typeName = typeof(T).FullName ?? typeof(T).Name;
        var payloadJson = JsonSerializer.Serialize(payload);

        var job = JobEntry.Create(typeName, payloadJson, priority, scheduledAt: scheduledAt, companyId: companyId);

        await using var uow = await _uowFactory.BeginAsync(ct);
        await uow.InsertJobAsync(job, ct);
        await uow.CommitAsync(ct);

        return job.Id;
    }
}
