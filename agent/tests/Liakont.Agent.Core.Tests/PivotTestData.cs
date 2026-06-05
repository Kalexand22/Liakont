namespace Liakont.Agent.Core.Tests;

using System;
using Liakont.Agent.Contracts.Pivot;

/// <summary>Constructeurs de données pivot FICTIVES pour les tests (aucune donnée client — CLAUDE.md n°7).</summary>
internal static class PivotTestData
{
    public static PivotDocumentDto Document(string sourceReference, DateTime issueDate, string? number = null)
    {
        return new PivotDocumentDto(
            sourceDocumentKind: "FAC",
            number: number ?? sourceReference,
            issueDate: issueDate,
            sourceReference: sourceReference,
            supplier: new PivotPartyDto("Vendeur fictif"),
            totals: new PivotTotalsDto(100m, 20m, 120m),
            operationCategory: OperationCategory.LivraisonBiens);
    }

    public static PivotPaymentDto Payment(DateTime paymentDate, decimal amount, string sourceReference)
    {
        return new PivotPaymentDto(paymentDate, amount, method: "CB", relatedDocumentNumber: null, sourceReference: sourceReference);
    }
}
