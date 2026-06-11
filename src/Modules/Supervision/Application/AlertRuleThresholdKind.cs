namespace Liakont.Modules.Supervision.Application;

/// <summary>
/// Champ d'application visé par le seuil d'une règle d'alerte — détermine comment le seuil EFFECTIF est
/// restitué à l'opérateur (FIX210, dispositif d'alerte visible). Le mapping seuil → champ
/// <c>AlertThresholdsDto</c> (TenantSettings/CFG02) est porté par la lecture du dispositif.
/// </summary>
public enum AlertRuleThresholdKind
{
    /// <summary>Heures de silence d'un agent (<c>AlertThresholdsDto.AgentSilentHours</c>).</summary>
    AgentSilentHours,

    /// <summary>Heures sans run d'extraction (<c>AlertThresholdsDto.MissedRunHours</c>).</summary>
    MissedRunHours,

    /// <summary>File de push : nombre d'éléments et âge (<c>PushQueueMaxItems</c> / <c>PushQueueMaxAgeHours</c>).</summary>
    PushQueue,

    /// <summary>Jours en état « bloqué » (<c>AlertThresholdsDto.BlockedDocumentsDays</c>).</summary>
    BlockedDocumentsDays,

    /// <summary>Jours en état « rejeté PA » (<c>AlertThresholdsDto.PaRejectionsDays</c>).</summary>
    PaRejectionsDays,

    /// <summary>Échéance déclarative fixe (J-3, non paramétrable).</summary>
    DeadlineFixed,

    /// <summary>Aucun seuil (règle binaire, ex. version d'agent obsolète).</summary>
    None,
}
