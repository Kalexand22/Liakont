namespace Liakont.Modules.Supervision.Application;

using System.Collections.Generic;
using Liakont.Modules.Supervision.Domain;

/// <summary>
/// Catalogue DÉCLARATIF des règles d'alerte de supervision (F12 §5.2 — l'unique source ; aucune règle
/// inventée). Sert à RESTITUER le dispositif complet à l'opérateur (FIX210) : règles actives ET gelées, avec
/// leur gravité et leur seuil. L'état actif/gelé n'est PAS figé ici : il se dérive des
/// <see cref="IAlertRule"/> réellement enregistrées (une règle gelée devenue implémentée s'affiche
/// automatiquement « active »). Les seuils par défaut sont ceux de F12 §5.2, dupliqués ici comme repli quand
/// le tenant n'a pas encore de seuils (CFG02) — la valeur de référence reste F12 §5.2.
/// </summary>
public static class AlertRuleCatalog
{
    /// <summary>Seuil « agent muet » par défaut (heures) — F12 §5.2.</summary>
    public const int DefaultAgentSilentHours = 24;

    /// <summary>Seuil « run d'extraction manqué » par défaut (heures) — F12 §5.2.</summary>
    public const int DefaultMissedRunHours = 36;

    /// <summary>Seuil « file de push » par défaut (éléments) — F12 §5.2.</summary>
    public const int DefaultPushQueueMaxItems = 50;

    /// <summary>Seuil « file de push » par défaut (âge en heures) — F12 §5.2.</summary>
    public const int DefaultPushQueueMaxAgeHours = 6;

    /// <summary>Seuil « documents bloqués » par défaut (jours) — F12 §5.2.</summary>
    public const int DefaultBlockedDocumentsDays = 5;

    /// <summary>Seuil « rejets PA » par défaut (jours) — F12 §5.2.</summary>
    public const int DefaultPaRejectionsDays = 2;

    /// <summary>Les 7 règles de F12 §5.2, dans l'ordre du tableau de la spec.</summary>
    public static readonly IReadOnlyList<AlertRuleDescriptor> All =
    [
        new AlertRuleDescriptor("agent.mute", "Agent muet (aucun heartbeat)", AlertSeverity.Critical, AlertRuleThresholdKind.AgentSilentHours),
        new AlertRuleDescriptor("agent.missed_run", "Run d'extraction manqué", AlertSeverity.Critical, AlertRuleThresholdKind.MissedRunHours),
        new AlertRuleDescriptor("push.queue_backlog", "File de push qui grossit", AlertSeverity.Warning, AlertRuleThresholdKind.PushQueue),
        new AlertRuleDescriptor("documents.blocked", "Documents bloqués non traités", AlertSeverity.Warning, AlertRuleThresholdKind.BlockedDocumentsDays),
        new AlertRuleDescriptor("documents.pa_rejected", "Rejets PA non traités", AlertSeverity.Critical, AlertRuleThresholdKind.PaRejectionsDays),
        new AlertRuleDescriptor("period.deadline_near", "Échéance de période déclarative proche", AlertSeverity.Critical, AlertRuleThresholdKind.DeadlineFixed),
        new AlertRuleDescriptor("agent.version_obsolete", "Version d'agent obsolète (< N-1)", AlertSeverity.Warning, AlertRuleThresholdKind.None),
    ];
}
