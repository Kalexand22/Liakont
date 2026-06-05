namespace Liakont.Agent.Core.Heartbeat;

using System;

/// <summary>
/// Photographie COHÉRENTE de l'issue du dernier run (lue en un seul verrou par
/// <see cref="AgentRunJournal.ReadSnapshot"/>) : les cinq champs proviennent du même instant, sans
/// risque de mélanger deux runs. Valeur pure.
/// </summary>
public sealed class AgentRunJournalSnapshot
{
    /// <summary>Crée une photographie d'issue de run.</summary>
    /// <param name="lastRunStartedUtc">Début du dernier run (UTC), si connu.</param>
    /// <param name="lastRunCompletedUtc">Fin du dernier run (UTC), si connu.</param>
    /// <param name="lastRunOutcome">Résultat du dernier run, si connu.</param>
    /// <param name="lastError">Dernière erreur locale, si connue.</param>
    /// <param name="lastSuccessfulSyncUtc">Dernier push réussi (UTC), si connu.</param>
    public AgentRunJournalSnapshot(
        DateTime? lastRunStartedUtc,
        DateTime? lastRunCompletedUtc,
        string? lastRunOutcome,
        string? lastError,
        DateTime? lastSuccessfulSyncUtc)
    {
        LastRunStartedUtc = lastRunStartedUtc;
        LastRunCompletedUtc = lastRunCompletedUtc;
        LastRunOutcome = lastRunOutcome;
        LastError = lastError;
        LastSuccessfulSyncUtc = lastSuccessfulSyncUtc;
    }

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
}
