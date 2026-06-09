namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Modules.Job.Contracts;

/// <summary>File de jobs qui lève à chaque mise en file (simulation d'une panne d'infrastructure).</summary>
internal sealed class ThrowingJobQueue : IJobQueue
{
    public Task<Guid> EnqueueAsync<T>(
        T payload,
        int priority = 0,
        DateTimeOffset? scheduledAt = null,
        Guid? companyId = null,
        CancellationToken ct = default) =>
        throw new InvalidOperationException("File de jobs indisponible (simulation).");
}
