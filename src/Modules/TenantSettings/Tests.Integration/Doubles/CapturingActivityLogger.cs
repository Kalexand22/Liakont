namespace Liakont.Modules.TenantSettings.Tests.Integration.Doubles;

using System.Collections.Concurrent;
using Stratum.Common.Abstractions.Audit;

/// <summary>Journal d'activité de test : capture les appels pour vérifier la journalisation des mutations.</summary>
internal sealed class CapturingActivityLogger : IActivityLogger
{
    public ConcurrentQueue<CapturedActivity> Entries { get; } = new();

    public Task LogActivityAsync(
        string entityType,
        string entityId,
        string activityType,
        string description,
        string actorId,
        object? metadata = null,
        Guid? companyId = null,
        CancellationToken cancellationToken = default)
    {
        Entries.Enqueue(new CapturedActivity(entityType, entityId, activityType, actorId, companyId));
        return Task.CompletedTask;
    }
}
