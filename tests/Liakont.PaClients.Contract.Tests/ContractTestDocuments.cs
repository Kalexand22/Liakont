namespace Liakont.PaClients.Contract.Tests;

using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Fabriques de documents pivot PARTAGÉES par la suite de contrat (montants en <see cref="decimal"/> —
/// CLAUDE.md n°1 ; valeurs fictives, aucune donnée client — CLAUDE.md n°7). Centralisées ici pour que
/// tout plug-in héritant <see cref="PaClientContractTests"/> exerce les MÊMES documents, et pour que
/// leur bonne forme soit gardée par un test autoportant (<c>ContractTestDocumentsTests</c>).
/// </summary>
internal static class ContractTestDocuments
{
    /// <summary>
    /// Facture de vente simple identifiée par son numéro (BT-1), avec un destinataire IDENTIFIÉ (SIREN
    /// fictif) et une ligne ventilée : certaines PA réelles (Super PDP, F14 §3.2) exigent un acheteur
    /// adressable et une catégorie de TVA par ligne — le document de contrat « valide » doit donc les
    /// porter pour exercer le chemin nominal de TOUT plug-in.
    /// </summary>
    /// <param name="number">Numéro du document.</param>
    public static PivotDocumentDto Invoice(string number) => new(
        sourceDocumentKind: "FACTURE",
        number: number,
        issueDate: new DateTime(2026, 1, 15),
        sourceReference: $"SRC-{number}",
        supplier: new PivotPartyDto("SVV Démo", siren: "123456789", vatNumber: "FR32123456789"),
        totals: new PivotTotalsDto(100m, 20m, 120m),
        operationCategory: OperationCategory.LivraisonBiens,
        customer: new PivotPartyDto("Client Démo", siren: "987654321"),
        lines: [new PivotLineDto("Prestation", 100m, taxes: [new PivotLineTaxDto(20m, 20m, VatCategory.S)])]);

    /// <summary>Avoir rattaché à une facture d'origine (porte une <see cref="PivotDocumentRefDto"/>).</summary>
    /// <param name="number">Numéro de l'avoir.</param>
    public static PivotDocumentDto CreditNote(string number) => new(
        sourceDocumentKind: "AVOIR",
        number: number,
        issueDate: new DateTime(2026, 2, 1),
        sourceReference: $"SRC-{number}",
        supplier: new PivotPartyDto("SVV Démo", siren: "123456789", vatNumber: "FR32123456789"),
        totals: new PivotTotalsDto(-50m, -10m, -60m),
        operationCategory: OperationCategory.LivraisonBiens,
        customer: new PivotPartyDto("Client Démo", siren: "987654321"),
        creditNoteRefs: [new PivotDocumentRefDto("F-ORIGINE", new DateTime(2026, 1, 10))]);
}
