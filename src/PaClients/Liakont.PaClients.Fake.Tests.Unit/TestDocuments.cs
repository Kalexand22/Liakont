namespace Liakont.PaClients.Fake.Tests.Unit;

using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Fabriques de documents pivot minimaux pour les tests du plug-in factice (montants en
/// <see cref="decimal"/> — CLAUDE.md n°1 ; valeurs fictives, aucune donnée client — CLAUDE.md n°7).
/// </summary>
internal static class TestDocuments
{
    /// <summary>Une facture de vente simple (pas d'avoir) identifiée par son numéro.</summary>
    public static PivotDocumentDto Invoice(string number) => new(
        sourceDocumentKind: "FACTURE",
        number: number,
        issueDate: new DateTime(2026, 1, 15),
        sourceReference: $"SRC-{number}",
        supplier: new PivotPartyDto("SVV Démo", siren: "123456789"),
        totals: new PivotTotalsDto(100m, 20m, 120m),
        operationCategory: OperationCategory.LivraisonBiens);

    /// <summary>
    /// Une auto-facture sous mandat (<c>IsSelfBilled</c>) : le <c>Supplier</c> EST le mandant (vendeur
    /// fiscal BG-4 → BT-30/BT-31), l'<c>Invoicer</c> est le tenant mandataire qui émet matériellement
    /// (art. 289 I-2 CGI). Valeurs fictives (CLAUDE.md n°7).
    /// </summary>
    public static PivotDocumentDto SelfBilled(
        string number,
        string mandantSiren = "404833048",
        string? mandantVatNumber = "FR40404833048") => new(
        sourceDocumentKind: "FACTURE",
        number: number,
        issueDate: new DateTime(2026, 1, 15),
        sourceReference: $"SRC-{number}",
        supplier: new PivotPartyDto("Armement Mandant Démo", siren: mandantSiren, vatNumber: mandantVatNumber),
        totals: new PivotTotalsDto(100m, 20m, 120m),
        operationCategory: OperationCategory.LivraisonBiens,
        invoicer: new PivotPartyDto("Étude Mandataire Démo", siren: "123456789"),
        isSelfBilled: true);

    /// <summary>Un avoir (rattaché à une facture d'origine) identifié par son numéro.</summary>
    public static PivotDocumentDto CreditNote(string number) => new(
        sourceDocumentKind: "AVOIR",
        number: number,
        issueDate: new DateTime(2026, 2, 1),
        sourceReference: $"SRC-{number}",
        supplier: new PivotPartyDto("SVV Démo", siren: "123456789"),
        totals: new PivotTotalsDto(-50m, -10m, -60m),
        operationCategory: OperationCategory.LivraisonBiens,
        creditNoteRefs: [new PivotDocumentRefDto("F-ORIGINE", new DateTime(2026, 1, 10))]);
}
