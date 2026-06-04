namespace Liakont.Modules.Reconciliation.Domain;

/// <summary>
/// État d'un PDF du pool dans la file d'attente de réconciliation (item TRK07). Persisté en
/// TEXTE (lisibilité d'audit, même motif que <c>DocumentState</c>).
/// </summary>
public enum ReconciliationStatus
{
    /// <summary>Rapproché AUTOMATIQUEMENT (confiance haute), lié et journalisé sans intervention.</summary>
    ReconciledAuto,

    /// <summary>Proposition de confiance moyenne EN ATTENTE de confirmation opérateur.</summary>
    PendingManual,

    /// <summary>Aucune correspondance, ou ambiguïté (≥ 2 candidats) : file d'attente manuelle (orphelin).</summary>
    Orphan,

    /// <summary>Rapproché MANUELLEMENT par un opérateur (confirmation d'une proposition ou rattachement d'un orphelin).</summary>
    ReconciledManual,
}
