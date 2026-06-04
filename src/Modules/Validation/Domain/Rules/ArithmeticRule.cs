namespace Liakont.Modules.Validation.Domain.Rules;

using Liakont.Agent.Contracts;
using Liakont.Modules.Validation.Contracts;

/// <summary>
/// BR-CO-15 (EN 16931, sévérité FATALE) : le total TTC (BT-112) doit égaler le total HT (BT-109) plus
/// le total TVA (BT-110). Réconciliation SANS tolérance, en <see cref="decimal"/>, après arrondi
/// commercial half-up à 2 décimales (<see cref="PivotRounding"/>). Une incohérence est BLOQUANTE :
/// envoyer un document dont l'arithmétique ne réconcilie pas garantit un rejet PA (CLAUDE.md n°3).
/// </summary>
public sealed class ArithmeticRule : IDocumentRule
{
    /// <summary>TTC ≠ HT + TVA (BR-CO-15).</summary>
    public const string MismatchCode = "DOC_ARITHMETIC_MISMATCH";

    /// <inheritdoc />
    public string Code => "ARITHMETIC_BR_CO_15";

    /// <inheritdoc />
    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var totals = context.Document.Totals;
        var expectedGross = PivotRounding.RoundAmount(totals.TotalNet + totals.TotalTax);
        var actualGross = PivotRounding.RoundAmount(totals.TotalGross);

        if (expectedGross == actualGross)
        {
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(Array.Empty<ValidationIssue>());
        }

        var issue = ValidationIssue.Blocking(
            MismatchCode,
            $"Le total TTC du document n° {context.Document.Number} ({RuleMessageFormat.FormatEuro(actualGross)}) ne correspond pas au total hors taxes plus la TVA ({RuleMessageFormat.FormatEuro(expectedGross)}). Vérifiez le document dans le logiciel source.",
            $"BR-CO-15 : TotalNet ({RuleMessageFormat.FormatInvariant(totals.TotalNet)}) + TotalTax ({RuleMessageFormat.FormatInvariant(totals.TotalTax)}) = {RuleMessageFormat.FormatInvariant(expectedGross)} ≠ TotalGross ({RuleMessageFormat.FormatInvariant(actualGross)}). Tolérance 0 (EN 16931).",
            "BT-112");

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(new[] { issue });
    }
}
