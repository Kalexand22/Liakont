namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Codes de type de document EN 16931 BT-3 (liste UNTDID 1001) PROJETÉS vers la Plateforme Agréée.
/// Ce sont les SEULES valeurs sourcées que le produit projette aujourd'hui — aucune n'est inventée
/// (CLAUDE.md n°2) : la classification facture/avoir reste portée par le pivot (<c>SourceDocumentKind</c>
/// brut + <c>IsSelfBilled</c>, ADR-0004 D3-3), ce type centralise seulement la valeur sortante.
/// </summary>
public static class PaDocumentTypeCode
{
    /// <summary>
    /// Facture commerciale (UNTDID 1001 « 380 »). Cas par défaut d'un document NON self-billed
    /// (EN 16931 BT-3 ; cf. <c>docs/architecture/mapping-pivot-en16931.md</c> §2, ligne BT-3).
    /// </summary>
    public const string CommercialInvoice = "380";

    /// <summary>
    /// Auto-facture sous mandat (UNTDID 1001 « 389 — Self-billed invoice : an invoice the invoicee is
    /// producing instead of the seller »). Type du SOCLE de démarrage DGFiP V3.2 (F15 §1.2/§1.3/§1.8,
    /// Annexe 7 G1.01) — projeté quand <c>PivotDocumentDto.IsSelfBilled</c> et que la PA déclare
    /// <see cref="PaCapabilities.SupportsSelfBilling"/>.
    /// </summary>
    public const string SelfBilledInvoice = "389";
}
