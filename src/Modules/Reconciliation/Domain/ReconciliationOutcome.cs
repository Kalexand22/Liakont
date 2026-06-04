namespace Liakont.Modules.Reconciliation.Domain;

/// <summary>
/// Verdict du moteur de rapprochement pour un PDF du pool (item TRK07).
/// </summary>
public enum ReconciliationOutcome
{
    /// <summary>Confiance HAUTE, candidat unique → lien AUTOMATIQUE (journalisé).</summary>
    AutoLinked,

    /// <summary>Confiance MOYENNE, candidat unique → PROPOSITION en file d'attente (confirmation requise).</summary>
    ProposeManual,

    /// <summary>Aucune correspondance, ou ambiguïté (≥ 2 candidats) → NON RÉCONCILIÉ (orphelin, file manuelle).</summary>
    NotReconciled,
}
