namespace Liakont.PaClients.Generique.Tests.Unit;

using Liakont.Agent.Contracts.Pivot;

/// <summary>Fabrique de documents pivot minimaux (valeurs fictives, montants decimal — CLAUDE.md n°1/7).</summary>
internal static class TestDocuments
{
    public static PivotDocumentDto Invoice(string number) => new(
        sourceDocumentKind: "FACTURE",
        number: number,
        issueDate: new DateTime(2026, 1, 15),
        sourceReference: $"SRC-{number}",
        supplier: new PivotPartyDto("SVV Démo", siren: "123456789"),
        totals: new PivotTotalsDto(100m, 20m, 120m),
        operationCategory: OperationCategory.LivraisonBiens);
}
