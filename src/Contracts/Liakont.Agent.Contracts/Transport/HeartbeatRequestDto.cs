namespace Liakont.Agent.Contracts.Transport;

using System;

/// <summary>
/// Battement de cœur émis par l'agent (POST /api/agent/v1/heartbeat — F12 §3.2). La plateforme
/// persiste l'état (rétention 90 jours, PIV05) et répond une <see cref="AgentConfigurationDto"/>.
/// <para>
/// La télémétrie d'exploitation (état du service, file, dernier run, erreurs, disque) est exigée par
/// F12 §2.5 et CONSOMMÉE par la supervision proactive (F12 §5.2 « File de push qui grossit »,
/// « Run d'extraction manqué » ; F12 §5.3 dashboard « dernier heartbeat, file, documents par état »).
/// Ces champs sont AJOUTÉS add-only (règle d'évolution v1, contrat-agent-v1.md §4.1) : ils sont
/// tous OPTIONNELS (un agent N-1 qui les omet reste compatible, la plateforme traite l'absence
/// comme « inconnu »). L'agent (AGT03) les PRODUIT ; leur persistance enrichie et leur exploitation
/// (dead-man's switch, dashboard) appartiennent aux items plateforme Ingestion/Supervision (SUP).
/// La frontière hash ne change pas : l'enveloppe heartbeat n'est JAMAIS hashée (contrat-agent-v1.md §3.2).
/// </para>
/// </summary>
public sealed class HeartbeatRequestDto
{
    /// <summary>Crée un battement de cœur.</summary>
    /// <param name="contractVersion">Version du contrat émise par l'agent.</param>
    /// <param name="agentVersion">Version de l'agent installé (pour la politique de mise à jour de flotte).</param>
    /// <param name="sentAtUtc">Horodatage d'émission (UTC).</param>
    /// <param name="lastSuccessfulSyncUtc">Dernier push réussi (UTC), si connu.</param>
    /// <param name="serviceState">État du service de l'agent (ex. « Running »), si connu.</param>
    /// <param name="pushQueueDepth">Nombre total d'éléments dans la file de push locale (F12 §5.2/§5.3), si connu.</param>
    /// <param name="pushQueueErrorCount">Nombre d'éléments en erreur dans la file (alimente l'alerte « erreurs répétées » F12 §5.2), si connu.</param>
    /// <param name="lastRunStartedUtc">Horodatage de début du dernier run d'extraction (UTC), si connu.</param>
    /// <param name="lastRunCompletedUtc">Horodatage de fin du dernier run d'extraction (UTC, alimente « Run manqué » F12 §5.2), si connu.</param>
    /// <param name="lastRunOutcome">Résultat du dernier run (ex. « Success », « SourceUnavailable », « SourceSchema »), si connu.</param>
    /// <param name="lastError">Dernière erreur locale lisible (diagnostic opérateur), si connue.</param>
    /// <param name="diskFreeBytes">Espace disque restant en octets sur le volume de la file locale, si connu.</param>
    public HeartbeatRequestDto(
        string contractVersion,
        string agentVersion,
        DateTime sentAtUtc,
        DateTime? lastSuccessfulSyncUtc = null,
        string? serviceState = null,
        int? pushQueueDepth = null,
        int? pushQueueErrorCount = null,
        DateTime? lastRunStartedUtc = null,
        DateTime? lastRunCompletedUtc = null,
        string? lastRunOutcome = null,
        string? lastError = null,
        long? diskFreeBytes = null)
    {
        ContractVersion = contractVersion;
        AgentVersion = agentVersion;
        SentAtUtc = sentAtUtc;
        LastSuccessfulSyncUtc = lastSuccessfulSyncUtc;
        ServiceState = serviceState;
        PushQueueDepth = pushQueueDepth;
        PushQueueErrorCount = pushQueueErrorCount;
        LastRunStartedUtc = lastRunStartedUtc;
        LastRunCompletedUtc = lastRunCompletedUtc;
        LastRunOutcome = lastRunOutcome;
        LastError = lastError;
        DiskFreeBytes = diskFreeBytes;
    }

    /// <summary>Version du contrat émise par l'agent.</summary>
    public string ContractVersion { get; }

    /// <summary>Version de l'agent installé.</summary>
    public string AgentVersion { get; }

    /// <summary>Horodatage d'émission (UTC).</summary>
    public DateTime SentAtUtc { get; }

    /// <summary>Dernier push réussi (UTC), si connu.</summary>
    public DateTime? LastSuccessfulSyncUtc { get; }

    /// <summary>État du service de l'agent (F12 §2.5 « état du service »).</summary>
    public string? ServiceState { get; }

    /// <summary>Taille de la file de push locale (F12 §2.5 « taille de la file » ; consommée F12 §5.2/§5.3).</summary>
    public int? PushQueueDepth { get; }

    /// <summary>Nombre d'éléments en erreur dans la file (alerte « erreurs répétées » F12 §5.2).</summary>
    public int? PushQueueErrorCount { get; }

    /// <summary>Horodatage de début du dernier run d'extraction (F12 §2.5 « horodatage du dernier run »).</summary>
    public DateTime? LastRunStartedUtc { get; }

    /// <summary>Horodatage de fin du dernier run d'extraction (alerte « Run manqué » F12 §5.2).</summary>
    public DateTime? LastRunCompletedUtc { get; }

    /// <summary>Résultat du dernier run (AGT03 ; ex. « Success », « SourceUnavailable »).</summary>
    public string? LastRunOutcome { get; }

    /// <summary>Dernière erreur locale lisible (F12 §2.5 « dernières erreurs »).</summary>
    public string? LastError { get; }

    /// <summary>Espace disque restant en octets sur le volume de la file locale (AGT03).</summary>
    public long? DiskFreeBytes { get; }
}
