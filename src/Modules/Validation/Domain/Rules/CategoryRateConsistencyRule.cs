namespace Liakont.Modules.Validation.Domain.Rules;

using System.Globalization;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;

/// <summary>
/// VAL04 — vérifie la cohérence catégorie UNCL5305 / taux de TVA, en BLOQUANT (F04 §3.4, AMENDÉE
/// 2026-06-02 : Warning → Blocking ; étendue 2026-06-03 aux 9 catégories du pivot). Une catégorie
/// incohérente avec son taux est soit une erreur de mapping, soit une donnée source fausse → dans les
/// deux cas, l'envoyer transmettrait un motif de taxation erroné (« bloquer plutôt qu'envoyer faux »,
/// CLAUDE.md n°3).
/// </summary>
/// <remarks>
/// Règles de cohérence (aucune inventée — F04 §3.4 + liste des catégories F03 §2.1) :
/// <list type="bullet">
/// <item>Catégories à taux POSITIF — <see cref="VatCategory.S"/> (normal), <see cref="VatCategory.AA"/>
/// (réduit), <see cref="VatCategory.AAA"/> (super réduit) : le taux doit être &gt; 0.</item>
/// <item>Catégories à taux ZÉRO — <see cref="VatCategory.Z"/>, <see cref="VatCategory.E"/>,
/// <see cref="VatCategory.AE"/>, <see cref="VatCategory.G"/>, <see cref="VatCategory.K"/>,
/// <see cref="VatCategory.O"/> : le taux doit être 0 (ou absent).</item>
/// </list>
/// La règle valide le document DÉJÀ MAPPÉ : une ventilation sans catégorie résolue
/// (<see cref="PivotLineTaxDto.CategoryCode"/> nul) n'est pas de son ressort (régime non mappé →
/// <see cref="MappingCoverageRule"/>). Pure, sans dépendance, sans écriture (détection seule).
/// QUESTION OUVERTE (expert-comptable, item VAL04) : un cas légitime de catégorie AA/AAA à taux 0 ?
/// Si oui, F04 §3.4 sera AMENDÉE — jamais assoupli silencieusement (CLAUDE.md n°2/3).
/// </remarks>
public sealed class CategoryRateConsistencyRule : IDocumentRule
{
    /// <summary>Code d'anomalie : catégorie de TVA incohérente avec son taux (F04 §3.4).</summary>
    public const string CategoryRateInconsistentCode = "CATEGORY_RATE_INCONSISTENT";

    private static readonly HashSet<VatCategory> PositiveRateCategories =
        new() { VatCategory.S, VatCategory.AA, VatCategory.AAA };

    private static readonly HashSet<VatCategory> ZeroRateCategories =
        new() { VatCategory.Z, VatCategory.E, VatCategory.AE, VatCategory.G, VatCategory.K, VatCategory.O };

    /// <inheritdoc />
    public string Code => "CATEGORY_RATE";

    /// <inheritdoc />
    /// <remarks>Dépend du mapping : la cohérence catégorie/taux ne se vérifie que sur la catégorie UNCL5305 posée par l'enrichissement.</remarks>
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
                if (tax.CategoryCode is not { } category)
                {
                    // Catégorie non résolue (régime non mappé) : hors périmètre, traité par MappingCoverageRule.
                    continue;
                }

                if (PositiveRateCategories.Contains(category))
                {
                    if (!tax.Rate.HasValue || tax.Rate.Value <= 0m)
                    {
                        issues.Add(BuildIssue(document.Number, category, tax.Rate, line, lineIndex, "un taux strictement positif"));
                    }
                }
                else if (ZeroRateCategories.Contains(category))
                {
                    // Taux absent = 0 (légitime pour une exonération) ; seul un taux non nul est incohérent.
                    if (tax.Rate.HasValue && tax.Rate.Value != 0m)
                    {
                        issues.Add(BuildIssue(document.Number, category, tax.Rate, line, lineIndex, "un taux nul (0)"));
                    }
                }
            }
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }

    private static ValidationIssue BuildIssue(string documentNumber, VatCategory category, decimal? rate, PivotLineDto line, int lineIndex, string expectation)
    {
        var message =
            $"La catégorie de TVA « {category} » est incohérente avec le taux ({FormatRate(rate)}) sur une ligne du " +
            $"document n° {documentNumber} : cette catégorie attend {expectation}. Vérifiez le mapping TVA " +
            "(Paramétrage › TVA) ou la donnée source ; le document reste bloqué tant que l'incohérence subsiste.";
        var detail = $"Ligne #{lineIndex + 1} « {line.Description} » : catégorie {category}, taux {FormatRate(rate)}.";
        return ValidationIssue.Blocking(CategoryRateInconsistentCode, message, detail, fieldRef: "BT-151/BT-152");
    }

    private static string FormatRate(decimal? rate)
        => rate.HasValue ? rate.Value.ToString(CultureInfo.InvariantCulture) + " %" : "absent";
}
