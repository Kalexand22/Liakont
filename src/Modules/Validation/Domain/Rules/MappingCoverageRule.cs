namespace Liakont.Modules.Validation.Domain.Rules;

using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;

/// <summary>
/// VAL04 — vérifie que chaque ligne porteuse d'un régime TVA source a bien été mappée vers une catégorie
/// UNCL5305 (F04 §3.4 : « Régime source mappé dans la table TVA », BLOQUANT — « régime inconnu → blocage,
/// jamais d'envoi à l'aveugle »). Filet de sécurité aval du moteur de mapping (F03, TVA02/TVA03) au sein
/// du pipeline d'envoi.
/// </summary>
/// <remarks>
/// La règle valide le document DÉJÀ MAPPÉ (post-F03) : le mapping plateforme renseigne
/// <see cref="PivotLineTaxDto.CategoryCode"/> ; l'agent le laisse TOUJOURS nul (frontière contrat). Donc,
/// pour une ligne qui DÉCLARE un régime source (<see cref="PivotLineDto.SourceRegimeCodes"/> non vide),
/// une ventilation sans catégorie résolue (aucune taxe, ou une taxe à catégorie nulle) signale un régime
/// NON COUVERT par la table du tenant → blocage. Le comportement est FAIL-SAFE : un document qui
/// atteindrait cette règle sans avoir été mappé est bloqué (jamais d'envoi à l'aveugle, CLAUDE.md n°3).
/// Pure, sans dépendance vers TvaMapping (elle constate le RÉSULTAT du mapping, elle ne re-mappe pas) ;
/// aucune écriture (détection seule) ; aucune règle fiscale inventée (CLAUDE.md n°2).
/// </remarks>
public sealed class MappingCoverageRule : IDocumentRule
{
    /// <summary>Code d'anomalie : régime source non couvert par la table de mapping TVA du tenant.</summary>
    public const string MappingCoverageMissingCode = "MAPPING_COVERAGE_MISSING";

    /// <inheritdoc />
    public string Code => "MAPPING_COVERAGE";

    /// <inheritdoc />
    /// <remarks>
    /// Dépend du mapping : elle CONSTATE l'absence de catégorie résolue. L'évaluer sur un document non encore
    /// mappé la ferait déclencher pour toute ligne (régime non résolu) — un motif redondant avec le blocage de
    /// mapping lui-même. Exclue de l'agrégation des motifs indépendants (FIX06).
    /// </remarks>
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

            // Seules les lignes qui déclarent un régime source sont concernées : l'absence totale de régime
            // source relève d'un autre contrôle (donnée source manquante), pas de la couverture du mapping.
            if (line.SourceRegimeCodes.Count == 0)
            {
                continue;
            }

            var hasUnresolvedVentilation = line.Taxes.Count == 0 || line.Taxes.Any(tax => tax.CategoryCode is null);
            if (hasUnresolvedVentilation)
            {
                var message =
                    $"Le régime de TVA source « {string.Join(", ", line.SourceRegimeCodes)} » d'une ligne du document " +
                    $"n° {document.Number} n'a pas de correspondance dans la table de mapping TVA du tenant " +
                    "(régime non mappé). Le document reste bloqué (jamais d'envoi à l'aveugle). Action : ajoutez " +
                    "une règle pour ce régime dans la console (Paramétrage › TVA), puis faites revalider la table " +
                    "par l'expert-comptable avant tout envoi.";
                var detail =
                    $"Ligne #{lineIndex + 1} « {line.Description} » : {line.Taxes.Count} ventilation(s) de TVA, catégorie UNCL5305 non résolue.";
                issues.Add(ValidationIssue.Blocking(MappingCoverageMissingCode, message, detail, fieldRef: "BT-151"));
            }
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
