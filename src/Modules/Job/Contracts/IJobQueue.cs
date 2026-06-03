namespace Stratum.Modules.Job.Contracts;

using System.Diagnostics.CodeAnalysis;

[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "IJobQueue is the domain-standard name for a job queue contract")]
public interface IJobQueue
{
    Task<Guid> EnqueueAsync<T>(
        T payload,
        int priority = 0,
        DateTimeOffset? scheduledAt = null,
        Guid? companyId = null,
        CancellationToken ct = default);
}
