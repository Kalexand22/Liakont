namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Document au format JSON <c>en16931</c> de Super PDP (schéma <c>en_invoice</c> de l'OpenAPI officielle
/// v1.24.0.beta — F14 §3.2, ✅ confirmé sandbox 2026-06-12). C'est le modèle sémantique EN 16931 : le
/// plug-in le construit depuis le pivot puis le fait CONVERTIR par Super PDP
/// (<c>POST /v1.beta/invoices/convert?from=en16931&amp;to=cii</c>) avant l'envoi XML — Liakont ne génère
/// jamais de CII/UBL lui-même. Montants en <see cref="decimal"/> (CLAUDE.md n°1), sérialisés en nombres
/// JSON sans perte. Champs requis du schéma : <c>number</c>, <c>issue_date</c>, <c>type_code</c>,
/// <c>currency_code</c>, <c>process_control</c>, <c>seller</c>, <c>buyer</c>, <c>totals</c>,
/// <c>vat_break_down</c>, <c>lines</c>.
/// </summary>
internal sealed record SuperPdpEnInvoice
{
    /// <summary>Numéro du document (EN 16931 BT-1).</summary>
    public required string Number { get; init; }

    /// <summary>Date d'émission au format <c>yyyy-MM-dd</c> (EN 16931 BT-2).</summary>
    public required string IssueDate { get; init; }

    /// <summary>
    /// Date d'échéance de paiement au format <c>yyyy-MM-dd</c> (EN 16931 BT-9) — OPTIONNELLE. Émise
    /// (<c>payment_due_date</c>) seulement quand le pivot la porte ; <c>null</c> ⇒ OMISE en écriture
    /// (<see cref="SuperPdpJson"/> <c>WhenWritingNull</c>), le converter rejette alors un montant dû
    /// positif par BR-CO-25 — message intact, jamais fabriquée (F14 §3.2/O11, EXT01).
    /// </summary>
    public string? PaymentDueDate { get; init; }

    /// <summary>
    /// Termes / conditions de paiement (EN 16931 BT-20) — OPTIONNEL, sérialisé <c>payment_terms</c>. Mention
    /// tenant (F12-A §3.4) ; satisfait BR-CO-25 pour un montant dû positif (alternative à BT-9). <c>null</c> ⇒
    /// OMIS en écriture (BUG-26, F16 §3.5).
    /// </summary>
    public string? PaymentTerms { get; init; }

    /// <summary>
    /// Notes de niveau document (EN 16931 BG-1) — OPTIONNEL, sérialisé <c>notes</c>. Porte les mentions
    /// légales FR obligatoires (BR-FR-05 : PMD/PMT/AAB), contenu = paramètre tenant. <c>null</c> ⇒ OMIS en
    /// écriture (BUG-26, F16 §3.5).
    /// </summary>
    public IReadOnlyList<SuperPdpEnInvoiceNote>? Notes { get; init; }

    /// <summary>
    /// Informations de livraison (EN 16931 BG-13) — OPTIONNEL, sérialisé <c>delivery_information</c>. Porte
    /// la date de livraison (BT-72) pour rendre l'élément livraison du CII non vide (PEPPOL-EN16931-R008).
    /// <c>null</c> ⇒ OMIS en écriture (BUG-26, F16 §3.5).
    /// </summary>
    public SuperPdpEnDeliveryInformation? DeliveryInformation { get; init; }

    /// <summary>Type de document UNTDID 1001 (EN 16931 BT-3) — <c>380</c> facture commerciale, en NOMBRE JSON.</summary>
    public required int TypeCode { get; init; }

    /// <summary>Devise ISO 4217 (EN 16931 BT-5).</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>Contexte de processus (EN 16931 BG-2) — porte l'identifiant de spécification BT-24.</summary>
    public required SuperPdpEnProcessControl ProcessControl { get; init; }

    /// <summary>Le vendeur (EN 16931 BG-4) — doit correspondre à l'entreprise du compte (F14 §3.2).</summary>
    public required SuperPdpEnParty Seller { get; init; }

    /// <summary>L'acheteur (EN 16931 BG-7) — doit être adressable dans l'annuaire (F14 §3.2).</summary>
    public required SuperPdpEnParty Buyer { get; init; }

    /// <summary>Totaux monétaires du document (EN 16931 BG-22).</summary>
    public required SuperPdpEnTotals Totals { get; init; }

    /// <summary>Ventilation de TVA par catégorie/taux (EN 16931 BG-23).</summary>
    public required IReadOnlyList<SuperPdpEnVatBreakDown> VatBreakDown { get; init; }

    /// <summary>Lignes du document (EN 16931 BG-25).</summary>
    public required IReadOnlyList<SuperPdpEnLine> Lines { get; init; }
}
