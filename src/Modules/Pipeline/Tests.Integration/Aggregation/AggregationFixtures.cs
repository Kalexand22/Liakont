namespace Liakont.Modules.Pipeline.Tests.Integration.Aggregation;

using System;
using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;

/// <summary>Fabriques de pivots pour les tests d'intégration de l'agrégation de paiement (PIP03a).</summary>
internal static class AggregationFixtures
{
    private static readonly string[] NormalRegime = { "NORMAL" };

    /// <summary>Construit un document PRESTATION DE SERVICES (mono-catégorie) : un ou plusieurs taux.</summary>
    public static PivotDocumentDto BuildServicePivot(string sourceReference, params (decimal NetAmount, decimal Tax, decimal Rate)[] lines)
    {
        var pivotLines = new List<PivotLineDto>();
        decimal totalNet = 0, totalTax = 0;
        var index = 1;
        foreach (var (netAmount, tax, rate) in lines)
        {
            pivotLines.Add(new PivotLineDto(
                description: "Prestation de services — frais ligne " + index,
                netAmount: netAmount,
                quantity: 1m,
                unitPriceNet: netAmount,
                sourceRegimeCodes: NormalRegime,
                taxes: new[] { new PivotLineTaxDto(tax, rate) },
                sourceLineRef: "ligne#" + index));
            totalNet += netAmount;
            totalTax += tax;
            index++;
        }

        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "F-2026-" + sourceReference,
            issueDate: new DateTime(2026, 1, 10),
            sourceReference: sourceReference,
            supplier: new PivotPartyDto("Étude Fictïve SVV"),
            totals: new PivotTotalsDto(totalNet, totalTax, totalNet + totalTax, totalNet + totalTax),
            operationCategory: OperationCategory.PrestationServices,
            lines: pivotLines);
    }
}
