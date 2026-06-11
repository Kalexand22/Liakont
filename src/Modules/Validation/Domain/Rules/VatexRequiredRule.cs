namespace Liakont.Modules.Validation.Domain.Rules;

using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;

/// <summary>
/// VAL04 — exige un code VATEX sur toute ligne exonérée (catégorie <see cref="VatCategory.E"/> à taux 0).
/// Source : F04 §3.4 (« Si catégorie E et taux 0 → code VATEX présent », BLOQUANT) — validé en staging
/// B2Brouter : l'absence de VATEX provoque un blocage SILENCIEUX côté PA, c'est exactement ce qu'on veut
/// attraper AVANT envoi. Le motif d'exonération est obligatoire en EN 16931 (BT-121) dès que la catégorie
/// est E.
/// </summary>
/// <remarks>
/// La règle valide le document DÉJÀ MAPPÉ (post-F03) : <see cref="PivotLineTaxDto.CategoryCode"/> et
/// <see cref="PivotLineTaxDto.VatexCode"/> sont le résultat du mapping plateforme (l'agent les laisse nuls
/// — frontière contrat). Pure : aucune dépendance externe, aucune écriture (détection seule). Aucune règle
/// fiscale inventée (CLAUDE.md n°2) : la condition vient mot pour mot de F04 §3.4.
/// PÉRIMÈTRE = catégorie E uniquement (cas confirmé en staging). EN 16931 (BR-AE-10/BR-G-10/BR-IC-10/BR-O-10)
/// exige aussi BT-121 pour AE/G/K/O ; étendre cette règle à ces catégories est une QUESTION OUVERTE tracée
/// dans F04 §3.4 (à trancher par l'expert-comptable) — jamais d'extension silencieuse (CLAUDE.md n°2).
/// </remarks>
public sealed class VatexRequiredRule : IDocumentRule
{
    /// <summary>Code d'anomalie : ligne exonérée (E, taux 0) sans code VATEX (F04 §5).</summary>
    public const string VatexMissingCode = "VATEX_MISSING";

    /// <inheritdoc />
    public string Code => "VATEX_REQUIRED";

    /// <inheritdoc />
    /// <remarks>Dépend du mapping : lit la catégorie UNCL5305 et le code VATEX posés par l'enrichissement (E + VATEX).</remarks>
    public bool DependsOnTvaMapping => true;

    /// <inheritdoc />
    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var document = context.Document;
        var issues = new List<ValidationIssue>();

        for (var lineIndex = 0; lineIndex < document.Lines.Count; lineIndex++)
        {
            var line = document.Lines[lineIndex];
            foreach (var tax in line.Taxes)
            {
                // Catégorie E (exonéré) à taux 0 (taux absent = 0 pour une exonération) : le motif VATEX
                // est obligatoire. Une incohérence E + taux > 0 relève de CategoryRateConsistencyRule.
                if (tax.CategoryCode == VatCategory.E
                    && (tax.Rate ?? 0m) == 0m
                    && string.IsNullOrWhiteSpace(tax.VatexCode))
                {
                    var message =
                        $"Une ligne du document n° {document.Number} est exonérée de TVA (catégorie E) mais le " +
                        "motif d'exonération (code VATEX) n'est pas déterminé. Complétez la table des régimes de " +
                        "TVA (Paramétrage › TVA) pour ce régime, puis revalidez le document.";
                    var detail =
                        $"Ligne #{lineIndex + 1} « {line.Description} » (régimes source : {DescribeRegimes(line)}) : catégorie E, taux {FormatRate(tax.Rate)}, VATEX absent.";
                    issues.Add(ValidationIssue.Blocking(VatexMissingCode, message, detail, fieldRef: "BT-121"));
                }
            }
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }

    private static string DescribeRegimes(PivotLineDto line)
        => line.SourceRegimeCodes.Count == 0 ? "aucun" : string.Join(", ", line.SourceRegimeCodes);

    private static string FormatRate(decimal? rate)
        => rate.HasValue ? rate.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "absent";
}
