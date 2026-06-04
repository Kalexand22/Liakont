namespace Liakont.Modules.Validation.Domain.Rules;

using Liakont.Agent.Contracts;
using Liakont.Modules.Validation.Contracts;

/// <summary>
/// Cohérence total passerelle ↔ total source (F04 §3.3, décision D5 du 2026-06-03) : si le document
/// source porte son propre total TTC brut (<c>Totals.SourceTotalGross</c>), un écart avec le total TTC
/// retenu par la passerelle est une ALERTE (Warning), jamais un blocage. Motif : un écart d'arrondi
/// légitime de la source ne doit pas empêcher l'envoi, mais l'opérateur est alerté — un écart répété
/// peut signaler un bug d'extraction à investiguer. Affaiblir un blocage métier serait un P1 ; ici le
/// niveau Warning est CELUI de la spec (F04 §3.3, décision #3), pas un affaiblissement.
/// </summary>
public sealed class SourceTotalsRule : IDocumentRule
{
    /// <summary>Total TTC passerelle ≠ total TTC source (alerte).</summary>
    public const string MismatchCode = "DOC_TOTAL_SOURCE_MISMATCH";

    /// <inheritdoc />
    public string Code => "SOURCE_TOTALS";

    /// <inheritdoc />
    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var totals = context.Document.Totals;

        // Pas de total source fourni : rien à comparer (champ absent = null, jamais une valeur par défaut).
        if (totals.SourceTotalGross is not decimal sourceGross)
        {
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(Array.Empty<ValidationIssue>());
        }

        var platformGross = PivotRounding.RoundAmount(totals.TotalGross);
        var roundedSource = PivotRounding.RoundAmount(sourceGross);
        if (platformGross == roundedSource)
        {
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(Array.Empty<ValidationIssue>());
        }

        var issue = ValidationIssue.Warning(
            MismatchCode,
            $"Le total TTC calculé par la passerelle pour le document n° {context.Document.Number} ({RuleMessageFormat.FormatEuro(platformGross)}) diffère du total indiqué par le logiciel source ({RuleMessageFormat.FormatEuro(roundedSource)}). Le document peut être envoyé ; un écart répété peut signaler un problème d'extraction à vérifier.",
            $"Totals.TotalGross = {RuleMessageFormat.FormatInvariant(platformGross)}, Totals.SourceTotalGross = {RuleMessageFormat.FormatInvariant(roundedSource)}.",
            "BT-112");

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(new[] { issue });
    }
}
