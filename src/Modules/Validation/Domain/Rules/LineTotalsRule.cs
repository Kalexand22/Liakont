namespace Liakont.Modules.Validation.Domain.Rules;

using Liakont.Agent.Contracts;
using Liakont.Modules.Validation.Contracts;

/// <summary>
/// Cohérence des totaux lignes ↔ totaux document (F04 §3.3) : la somme des montants hors taxes des
/// lignes doit égaler le total HT (EN 16931 BT-109) et la somme de la TVA des lignes le total TVA
/// (BT-110). Réconciliation SANS tolérance (EN 16931), en <see cref="decimal"/>, après arrondi
/// commercial half-up à 2 décimales (<see cref="PivotRounding"/>). Toute incohérence est BLOQUANTE :
/// un écart, même d'un centime d'arrondi, n'est jamais rattrapé silencieusement (CLAUDE.md n°1, n°3).
/// </summary>
public sealed class LineTotalsRule : IDocumentRule
{
    /// <summary>Σ HT des lignes ≠ total HT du document.</summary>
    public const string NetMismatchCode = "DOC_TOTAL_MISMATCH";

    /// <summary>Σ TVA des lignes ≠ total TVA du document.</summary>
    public const string TaxMismatchCode = "DOC_VAT_TOTAL_MISMATCH";

    /// <inheritdoc />
    public string Code => "LINE_TOTALS";

    /// <inheritdoc />
    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var document = context.Document;

        // Sans ligne, la réconciliation n'a pas de sens : StructureRule porte le blocage « aucune ligne ».
        if (document.Lines.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(Array.Empty<ValidationIssue>());
        }

        var issues = new List<ValidationIssue>();

        var lineNet = PivotRounding.RoundAmount(document.Lines.Sum(line => line.NetAmount));
        var totalNet = PivotRounding.RoundAmount(document.Totals.TotalNet);
        if (lineNet != totalNet)
        {
            issues.Add(ValidationIssue.Blocking(
                NetMismatchCode,
                $"Le total hors taxes du document n° {document.Number} ({RuleMessageFormat.FormatEuro(totalNet)}) ne correspond pas à la somme des lignes ({RuleMessageFormat.FormatEuro(lineNet)}). Vérifiez le document dans le logiciel source.",
                $"Σ lignes HT = {RuleMessageFormat.FormatInvariant(lineNet)}, Totals.TotalNet = {RuleMessageFormat.FormatInvariant(totalNet)} (arrondi half-up 2 déc., tolérance 0).",
                "BT-109"));
        }

        var lineTax = PivotRounding.RoundAmount(document.Lines.Sum(line => line.Taxes.Sum(tax => tax.TaxAmount)));
        var totalTax = PivotRounding.RoundAmount(document.Totals.TotalTax);
        if (lineTax != totalTax)
        {
            issues.Add(ValidationIssue.Blocking(
                TaxMismatchCode,
                $"Le total de TVA du document n° {document.Number} ({RuleMessageFormat.FormatEuro(totalTax)}) ne correspond pas à la somme de la TVA des lignes ({RuleMessageFormat.FormatEuro(lineTax)}). Vérifiez le document dans le logiciel source.",
                $"Σ lignes TVA = {RuleMessageFormat.FormatInvariant(lineTax)}, Totals.TotalTax = {RuleMessageFormat.FormatInvariant(totalTax)} (arrondi half-up 2 déc., tolérance 0).",
                "BT-110"));
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
