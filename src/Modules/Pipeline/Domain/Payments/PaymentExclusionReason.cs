namespace Liakont.Modules.Pipeline.Domain.Payments;

/// <summary>
/// Raison pour laquelle un encaissement n'entre PAS dans les agrégats jour×taux de PIP03a. Aucune de ces
/// raisons n'invente de règle : elles SUSPENDENT ou ÉCARTENT plutôt que de deviner (CLAUDE.md n°2/3).
/// </summary>
public enum PaymentExclusionReason
{
    /// <summary>Règlement non rattaché à un bordereau (référence absente ou document introuvable) — alerte Warning (F09 §5.4).</summary>
    Unattached,

    /// <summary>Aucun snapshot de ventilation pour la version de mapping du document (re-CHECK requis).</summary>
    SnapshotMissing,

    /// <summary>Document Mixte : découpage frais/adjudication non sourcé (D-b) — suspendu (réservé à PIP03b).</summary>
    MixteSuspended,

    /// <summary>Autoliquidation (reverse charge, catégorie AE) : exclue de l'e-reporting de paiement (F09 §2).</summary>
    ReverseCharge,

    /// <summary>Livraison de biens : exigibilité à la livraison, pas d'e-reporting de paiement (non requis).</summary>
    GoodsNotApplicable,

    /// <summary>Taux de TVA non résolu dans la ventilation : impossible de ventiler l'encaissement par taux.</summary>
    UnresolvedRate,

    /// <summary>Total document nul : impossible de calculer la couverture de l'encaissement.</summary>
    ZeroDocumentTotal,
}
