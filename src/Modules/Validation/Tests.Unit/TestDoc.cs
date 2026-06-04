namespace Liakont.Modules.Validation.Tests.Unit;

using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;

/// <summary>
/// Fabriques de documents pivot pour les tests des règles VAL03. Valeurs par défaut : un bordereau
/// cohérent (une ligne 1000 HT / 200 TVA, total 1200 TTC, EUR, daté du 15/01/2024).
/// </summary>
internal static class TestDoc
{
    /// <summary>Crée une ligne pivot (HT, TVA, taux optionnel).</summary>
    public static PivotLineDto Line(decimal net, decimal tax, decimal? rate = 20m) =>
        new("Lot", net, taxes: new[] { new PivotLineTaxDto(tax, rate) });

    /// <summary>Crée une charge (par défaut) ou remise de niveau document (montant HT).</summary>
    public static PivotDocumentChargeDto Charge(decimal amount, bool isCharge = true) =>
        new(isCharge, amount);

    /// <summary>Crée un contexte de validation, chaque facette étant surchargeable.</summary>
    public static DocumentValidationContext Context(
        string number = "2019",
        DateTime? issueDate = null,
        string currencyCode = "EUR",
        decimal totalNet = 1000m,
        decimal totalTax = 200m,
        decimal totalGross = 1200m,
        decimal? sourceTotalGross = null,
        IReadOnlyList<PivotLineDto>? lines = null,
        IReadOnlyList<PivotDocumentChargeDto>? charges = null,
        Guid? companyId = null)
    {
        var document = new PivotDocumentDto(
            sourceDocumentKind: "BORDEREAU",
            number: number,
            issueDate: issueDate ?? new DateTime(2024, 1, 15),
            sourceReference: "src",
            supplier: new PivotPartyDto("Étude Fictive SVV"),
            totals: new PivotTotalsDto(totalNet, totalTax, totalGross, sourceTotalGross),
            operationCategory: OperationCategory.LivraisonBiens,
            currencyCode: currencyCode,
            documentCharges: charges,
            lines: lines ?? new[] { Line(1000m, 200m) });
        return new DocumentValidationContext(document, companyId ?? Guid.NewGuid());
    }
}
