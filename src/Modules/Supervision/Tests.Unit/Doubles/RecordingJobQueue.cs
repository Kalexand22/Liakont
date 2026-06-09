namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Modules.Job.Contracts;
using Stratum.Modules.Notification.Contracts.DTOs;

/// <summary>
/// File de jobs factice : capture chaque charge utile enfilée (et son <c>companyId</c>) pour assertion.
/// Expose un raccourci sur les charges <see cref="EmailSendJobPayload"/> (le seul type enfilé par SUP03).
/// </summary>
internal sealed class RecordingJobQueue : IJobQueue
{
    private readonly List<(object Payload, Guid? CompanyId)> _enqueued = new();

    public IReadOnlyList<(object Payload, Guid? CompanyId)> Enqueued => _enqueued;

    public IReadOnlyList<EmailSendJobPayload> Emails =>
        _enqueued.Select(e => e.Payload).OfType<EmailSendJobPayload>().ToList();

    public Task<Guid> EnqueueAsync<T>(
        T payload,
        int priority = 0,
        DateTimeOffset? scheduledAt = null,
        Guid? companyId = null,
        CancellationToken ct = default)
    {
        _enqueued.Add((payload!, companyId));
        return Task.FromResult(Guid.NewGuid());
    }
}
