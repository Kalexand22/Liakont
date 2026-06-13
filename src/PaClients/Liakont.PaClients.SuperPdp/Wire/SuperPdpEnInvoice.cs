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
