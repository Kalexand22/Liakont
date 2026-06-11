namespace Liakont.Modules.Validation.Domain.Rules;

using Liakont.Agent.Contracts;
using Liakont.Modules.Validation.Contracts;

/// <summary>
/// Cohérence des totaux lignes ↔ totaux document (F04 §3.3) : le total HT (EN 16931 BT-109) doit
/// égaler la somme des lignes (BT-131) corrigée des remises (BG-20) et charges (BG-21) de niveau
/// document — BR-CO-13 ; et le total TVA (BT-110) la somme de la TVA des lignes. Réconciliation SANS
/// tolérance (EN 16931), en <see cref="decimal"/>, après arrondi commercial half-up à 2 décimales
/// (<see cref="PivotRounding"/>). Toute incohérence est BLOQUANTE : un écart, même d'un centime
/// d'arrondi, n'est jamais rattrapé silencieusement (CLAUDE.md n°1, n°3). Limite connue : la TVA des
/// charges/remises de niveau document n'étant pas résolue à ce stade (mapping en TVA04), la
/// réconciliation TVA n'est exécutée que lorsque le document ne porte pas de charge/remise globale
/// (tracé F04 §3.3) — sans quoi elle produirait un faux positif bloquant.
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

        // BR-CO-13 : Total HT (BT-109) = Σ lignes HT (BT-131) − Σ remises document (BG-20)
        // + Σ charges document (BG-21). Formule centralisée dans PivotReconciliation (source UNIQUE
        // partagée avec l'affichage console FIX205, pour qu'elles ne divergent jamais).
        var expectedNet = PivotReconciliation.ExpectedNet(document);
        var totalNet = PivotRounding.RoundAmount(document.Totals.TotalNet);
        if (expectedNet != totalNet)
        {
            issues.Add(ValidationIssue.Blocking(
                NetMismatchCode,
                $"Le total hors taxes du document n° {document.Number} ({RuleMessageFormat.FormatEuro(totalNet)}) ne correspond pas à la somme des lignes corrigée des remises et charges de niveau document ({RuleMessageFormat.FormatEuro(expectedNet)}). Vérifiez le document dans le logiciel source.",
                $"Σ lignes HT ± charges/remises document = {RuleMessageFormat.FormatInvariant(expectedNet)}, Totals.TotalNet = {RuleMessageFormat.FormatInvariant(totalNet)} (BR-CO-13, arrondi half-up 2 déc., tolérance 0).",
                "BT-109"));
        }

        // La TVA des charges/remises de niveau document n'est pas résolue en VAL03 (codes régime source
        // non encore mappés — cf. TVA04). Sans elle, Σ TVA des lignes ≠ Total TVA serait un faux positif
        // dès qu'un document porte une charge/remise globale ; on ne réconcilie donc la TVA que lorsque
        // le document n'en porte pas. Limite connue tracée dans F04 §3.3.
        if (document.DocumentCharges.Count == 0)
        {
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
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
