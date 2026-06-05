namespace Liakont.PaClients.B2Brouter.Wire;

/// <summary>
/// Facture B2Brouter (modèle d'import). En V1, B2C = <c>IssuedSimplifiedInvoice</c> (F07-F08 :
/// l'avoir B2C s'envoie en <c>IssuedSimplifiedInvoice</c> avec <c>is_credit_note: true</c>). Les
/// montants restent en <see cref="decimal"/> côté lignes (CLAUDE.md n°1). Les noms de champs « fil »
/// (enveloppe, lignes, taxe) sont confirmés bout-en-bout par la suite staging de PAB04.
/// </summary>
internal sealed record B2BrouterInvoice
{
    /// <summary>Type de document B2Brouter (V1 B2C : <c>IssuedSimplifiedInvoice</c>).</summary>
    public required string Type { get; init; }

    /// <summary>Numéro du document (EN 16931 BT-1) — clé d'unicité côté B2Brouter (F05 §4.2).</summary>
    public required string Number { get; init; }

    /// <summary>Date d'émission au format <c>yyyy-MM-dd</c> (EN 16931 BT-2).</summary>
    public required string Date { get; init; }

    /// <summary>Devise ISO 4217 (EN 16931 BT-5).</summary>
    public required string Currency { get; init; }

    /// <summary>Vrai = créer ET envoyer ; faux = créé sans envoi, état <c>new</c> (F05 §2).</summary>
    public required bool SendAfterImport { get; init; }

    /// <summary>Vrai si le document est un avoir (F05 ; F07-F08).</summary>
    public bool IsCreditNote { get; init; }

    /// <summary>Vrai pour un avoir rectificatif (omis pour une facture normale — F05).</summary>
    public bool? IsAmend { get; init; }

    /// <summary>Numéro de la facture d'origine rectifiée (avoir uniquement — F05).</summary>
    public string? AmendedNumber { get; init; }

    /// <summary>Date <c>yyyy-MM-dd</c> de la facture d'origine rectifiée (avoir uniquement — F05).</summary>
    public string? AmendedDate { get; init; }

    /// <summary>Lignes de la facture (modèle « 2 lignes marge » validé en staging — F03 §2.3).</summary>
    public required IReadOnlyList<B2BrouterInvoiceLine> InvoiceLines { get; init; }
}
