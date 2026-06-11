namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Facture Super PDP (modèle d'émission). En V1, Liakont est B2C : la facture part en facture
/// simplifiée émise (même modèle que B2Brouter F07-F08). Les montants restent en <see cref="decimal"/>
/// (CLAUDE.md n°1). Les noms de champs « fil » sont la CIBLE de conception (F14 §3.2) — confirmés
/// bout-en-bout par la suite sandbox de PAS03. Aucun avoir n'est émis en V1 (capacité
/// <see cref="Modules.Transmission.Contracts.PaCapabilities.SupportsCreditNotes"/> = <c>false</c>, F14 §5).
/// </summary>
internal sealed record SuperPdpInvoice
{
    /// <summary>Type de document Super PDP (V1 B2C : facture simplifiée émise — cible de conception F14 §3.2).</summary>
    public required string Type { get; init; }

    /// <summary>Numéro du document (EN 16931 BT-1) — clé d'unicité côté Super PDP (F14 §4.1).</summary>
    public required string Number { get; init; }

    /// <summary>Date d'émission au format <c>yyyy-MM-dd</c> (EN 16931 BT-2).</summary>
    public required string Date { get; init; }

    /// <summary>Devise ISO 4217 (EN 16931 BT-5).</summary>
    public required string Currency { get; init; }

    /// <summary>
    /// Vrai = créer ET envoyer ; faux = créé sans envoi (équivalent du <c>send_after_import</c> B2Brouter,
    /// mécanisme exact à confirmer sandbox — F14 §3.2, O7).
    /// </summary>
    public required bool SendAfterImport { get; init; }

    /// <summary>Lignes de la facture (modèle « 2 lignes marge » — F03 §2.3, recopié du pivot).</summary>
    public required IReadOnlyList<SuperPdpInvoiceLine> InvoiceLines { get; init; }
}
