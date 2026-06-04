namespace Liakont.Modules.Reconciliation.Domain;

/// <summary>
/// Stratégie de rapprochement ayant produit une correspondance (item TRK07). Tracée dans la
/// file d'attente et dans la piste d'audit pour expliquer POURQUOI un PDF a été rattaché à un document.
/// </summary>
public enum MatchStrategy
{
    /// <summary>Confiance HAUTE : le numéro de document figure dans le NOM DE FICHIER du PDF.</summary>
    FileName,

    /// <summary>Confiance HAUTE : le numéro de document figure dans le TEXTE extrait du PDF.</summary>
    PdfContent,

    /// <summary>Confiance MOYENNE : la date d'émission ET le montant TTC du document figurent dans le PDF (candidat unique).</summary>
    DateAndAmount,
}
