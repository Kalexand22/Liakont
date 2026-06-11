namespace Liakont.Host.Supervision;

/// <summary>État de santé du dead-man's-switch de supervision (F12 §5.1), pour le témoin de vie affiché en
/// tête des pages de supervision (FIX210). Distingue une supervision SAINE d'une supervision MUETTE — le
/// symptôme « aucune alerte récente » est indiscernable d'un filet de sécurité en panne sans ce témoin.</summary>
public enum SupervisionLivenessStatus
{
    /// <summary>Le dispositif a été évalué récemment (dans la fenêtre attendue).</summary>
    Healthy,

    /// <summary>Aucune évaluation depuis plus que la fenêtre tolérée — le filet de sécurité est peut-être en panne.</summary>
    Overdue,

    /// <summary>Le dispositif n'a JAMAIS été évalué (aucune planification active ou aucune exécution).</summary>
    NeverEvaluated,

    /// <summary>État indéterminé (lecture impossible) — affiché sans fausse alerte.</summary>
    Unknown,
}
