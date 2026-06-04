namespace Liakont.Modules.Validation.Tests.Unit;

using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;

/// <summary>
/// Fabriques de documents pivot FICTIFS pour les tests des règles VAL04. Codes régimes et montants sont
/// des exemples génériques, jamais des données client (CLAUDE.md n°7).
/// </summary>
internal static class PivotDocumentBuilder
{
    /// <summary>Encapsule un document dans un contexte de validation tenant-scopé.</summary>
    public static DocumentValidationContext Context(PivotDocumentDto document, Guid? companyId = null)
        => new(document, companyId ?? Guid.NewGuid());

    /// <summary>Construit un document pivot fictif avec des valeurs par défaut neutres.</summary>
    public static PivotDocumentDto Document(
        string number = "2019",
        IReadOnlyList<PivotLineDto>? lines = null,
        IReadOnlyList<PivotDocumentRefDto>? creditNoteRefs = null,
        PivotTotalsDto? totals = null,
        decimal? prepaidAmount = null)
        => new(
            sourceDocumentKind: "BORDEREAU",
            number: number,
            issueDate: new DateTime(2024, 1, 15),
            sourceReference: "src-" + number,
            supplier: new PivotPartyDto("Étude Fictive SVV"),
            totals: totals ?? new PivotTotalsDto(0m, 0m, 0m),
            operationCategory: OperationCategory.LivraisonBiens,
            lines: lines,
            creditNoteRefs: creditNoteRefs,
            prepaidAmount: prepaidAmount);

    /// <summary>Construit une ligne pivot fictive.</summary>
    public static PivotLineDto Line(
        decimal netAmount = 100m,
        IReadOnlyList<string>? sourceRegimeCodes = null,
        IReadOnlyList<PivotLineTaxDto>? taxes = null,
        string description = "Adjudication lot fictif")
        => new(description, netAmount, sourceRegimeCodes: sourceRegimeCodes, taxes: taxes);

    /// <summary>Construit une ventilation de TVA de ligne (résultat de mapping simulé).</summary>
    public static PivotLineTaxDto Tax(
        decimal taxAmount = 20m,
        decimal? rate = 20m,
        VatCategory? category = VatCategory.S,
        string? vatex = null)
        => new(taxAmount, rate, category, vatex);

    /// <summary>Construit une référence de facture d'origine d'avoir.</summary>
    public static PivotDocumentRefDto OriginalRef(string number = "2018", string? sourceReference = null)
        => new(number, new DateTime(2024, 1, 10), sourceReference);
}
