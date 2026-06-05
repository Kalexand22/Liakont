namespace Liakont.Agent.Core.Heartbeat;

using System;

/// <summary>
/// Photographie de l'état d'exploitation de l'agent à un instant donné (F12 §2.5), assemblée à partir
/// de la file locale, du <see cref="AgentRunJournal"/> et de la sonde disque, puis remontée dans le
/// heartbeat. Valeur PURE (aucune logique métier — l'agent ne fait que rapporter).
/// </summary>
public sealed class AgentHealthSnapshot
{
    /// <summary>Crée une photographie d'état.</summary>
    /// <param name="serviceState">État du service (ex. « Running »).</param>
    /// <param name="pushQueueDepth">Nombre total d'éléments dans la file de push.</param>
    /// <param name="pushQueueErrorCount">Nombre d'éléments en erreur dans la file.</param>
    /// <param name="lastRunStartedUtc">Début du dernier run (UTC), si connu.</param>
    /// <param name="lastRunCompletedUtc">Fin du dernier run (UTC), si connu.</param>
    /// <param name="lastRunOutcome">Résultat du dernier run, si connu.</param>
    /// <param name="lastError">Dernière erreur locale, si connue.</param>
    /// <param name="lastSuccessfulSyncUtc">Dernier push réussi (UTC), si connu.</param>
    /// <param name="diskFreeBytes">Espace disque restant en octets, si mesurable.</param>
    public AgentHealthSnapshot(
        string serviceState,
        int pushQueueDepth,
        int pushQueueErrorCount,
        DateTime? lastRunStartedUtc,
        DateTime? lastRunCompletedUtc,
        string? lastRunOutcome,
        string? lastError,
        DateTime? lastSuccessfulSyncUtc,
        long? diskFreeBytes)
    {
        ServiceState = serviceState;
        PushQueueDepth = pushQueueDepth;
        PushQueueErrorCount = pushQueueErrorCount;
        LastRunStartedUtc = lastRunStartedUtc;
        LastRunCompletedUtc = lastRunCompletedUtc;
        LastRunOutcome = lastRunOutcome;
        LastError = lastError;
        LastSuccessfulSyncUtc = lastSuccessfulSyncUtc;
        DiskFreeBytes = diskFreeBytes;
    }

    /// <summary>État du service de l'agent.</summary>
    public string ServiceState { get; }

    /// <summary>Nombre total d'éléments dans la file de push.</summary>
    public int PushQueueDepth { get; }

    /// <summary>Nombre d'éléments en erreur dans la file.</summary>
    public int PushQueueErrorCount { get; }

    /// <summary>Début du dernier run (UTC).</summary>
    public DateTime? LastRunStartedUtc { get; }

    /// <summary>Fin du dernier run (UTC).</summary>
    public DateTime? LastRunCompletedUtc { get; }

    /// <summary>Résultat du dernier run.</summary>
    public string? LastRunOutcome { get; }

    /// <summary>Dernière erreur locale.</summary>
    public string? LastError { get; }

    /// <summary>Dernier push réussi (UTC).</summary>
    public DateTime? LastSuccessfulSyncUtc { get; }

    /// <summary>Espace disque restant en octets.</summary>
    public long? DiskFreeBytes { get; }
}
