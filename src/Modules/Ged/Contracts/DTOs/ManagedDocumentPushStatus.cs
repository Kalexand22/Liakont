namespace Liakont.Modules.Ged.Contracts.DTOs;

/// <summary>
/// Verdict d'ingestion d'un document géré (canal GED, F19 §2.4/§4.3). L'INDEXATION (mapping → axes/entités/liens ou
/// déférement) est ASYNCHRONE (déclenchée par l'événement <c>ManagedDocumentReceivedV1</c>) : ce verdict porte
/// uniquement la RÉCEPTION (accepté/doublon/rejeté), jamais l'issue de l'indexation.
/// </summary>
public enum ManagedDocumentPushStatus
{
    /// <summary>Document accepté (nouveau ou altéré) : registre + événement écrits, indexation déclenchée.</summary>
    Accepted,

    /// <summary>Empreinte déjà reçue pour ce tenant : aucune réécriture, aucun événement (idempotent).</summary>
    Duplicate,

    /// <summary>Document rejeté à la frontière (champ obligatoire absent, sérialisation impossible) — jamais un 500.</summary>
    Rejected,
}
