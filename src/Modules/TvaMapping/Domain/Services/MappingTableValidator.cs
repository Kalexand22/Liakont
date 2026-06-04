namespace Liakont.Modules.TvaMapping.Domain.Services;

using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.TvaMapping.Domain.Entities;

/// <summary>
/// Validation structurelle d'une table de mapping TVA (item TVA01 §3, F03 §2/§4). Appliquée à la
/// création (écriture) ET au chargement — une table invalide n'est jamais silencieusement acceptée
/// (CLAUDE.md n°3). Le validateur n'invente aucune règle fiscale : il vérifie uniquement les
/// contraintes énoncées par la spec (catégories UNCL5305, E à 0 % → VATEX, cohérence du taux,
/// absence de doublon). Les décisions de mapping elles-mêmes appartiennent à l'expert-comptable.
/// </summary>
public static class MappingTableValidator
{
    /// <summary>
    /// Retourne la liste des violations structurelles (vide si la table est valide). Toutes les
    /// violations sont collectées d'un coup pour un message opérateur complet.
    /// </summary>
    /// <param name="mappingVersion">Version de la table (obligatoire, traçabilité F03 §5).</param>
    /// <param name="defaultBehavior">Comportement par défaut (doit être une valeur définie).</param>
    /// <param name="rules">Règles de la table.</param>
    public static IReadOnlyList<string> Validate(
        string? mappingVersion,
        MappingDefaultBehavior defaultBehavior,
        IReadOnlyList<MappingRule> rules)
    {
        var violations = new List<string>();

        if (string.IsNullOrWhiteSpace(mappingVersion))
        {
            violations.Add("la version de table (mappingVersion) est obligatoire");
        }

        if (!Enum.IsDefined(defaultBehavior))
        {
            violations.Add(
                $"le comportement par défaut est inconnu ({(int)defaultBehavior}) ; seul « block » est admis (F03 §4.1)");
        }

        ArgumentNullException.ThrowIfNull(rules);

        var seen = new HashSet<(string Code, MappingPart Part)>();
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var n = i + 1;

            if (rule is null)
            {
                violations.Add($"règle #{n} : règle absente (null)");
                continue;
            }

            if (string.IsNullOrWhiteSpace(rule.SourceRegimeCode))
            {
                violations.Add($"règle #{n} : code régime source obligatoire");
            }

            if (!Enum.IsDefined(rule.Category))
            {
                violations.Add(
                    $"règle #{n} : catégorie de TVA inconnue ({(int)rule.Category}). Catégories admises : " +
                    string.Join(", ", VatCategoryParser.AllowedCodes) + " (F03 §2.1)");
            }

            if (!Enum.IsDefined(rule.Part))
            {
                violations.Add($"règle #{n} : part inconnue ({(int)rule.Part})");
            }

            ValidateRate(rule, n, violations);

            // F03 §2.2 : le motif VATEX est obligatoire DÈS QUE la catégorie est E (exonéré), quel
            // que soit le mode de taux — sans VATEX, l'administration recevrait une exonération sans
            // justification. Une exonération étant par définition à 0 %, un taux fixe non nul sur E
            // est fiscalement incohérent.
            if (Enum.IsDefined(rule.Category) && rule.Category == VatCategory.E)
            {
                if (string.IsNullOrWhiteSpace(rule.Vatex))
                {
                    violations.Add(
                        $"règle #{n} : catégorie E (exonéré) sans code VATEX — un motif d'exonération est obligatoire (F03 §2.2)");
                }

                if (rule.RateMode == RateMode.Fixed && rule.RateValue is { } exemptRate && exemptRate != 0m)
                {
                    violations.Add(
                        $"règle #{n} : catégorie E (exonéré) avec un taux fixe non nul ({rule.RateValue}) — une exonération est à 0 %");
                }
            }

            if (!string.IsNullOrWhiteSpace(rule.SourceRegimeCode)
                && !seen.Add((rule.SourceRegimeCode, rule.Part)))
            {
                violations.Add(
                    $"règle #{n} : doublon de règle pour le code régime « {rule.SourceRegimeCode} » et la part « {rule.Part} »");
            }
        }

        return violations;
    }

    /// <summary>
    /// Vérifie la table et lève <see cref="InvalidMappingTableException"/> si elle est invalide.
    /// </summary>
    public static void EnsureValid(
        string? mappingVersion,
        MappingDefaultBehavior defaultBehavior,
        IReadOnlyList<MappingRule> rules)
    {
        var violations = Validate(mappingVersion, defaultBehavior, rules);
        if (violations.Count > 0)
        {
            throw new InvalidMappingTableException(violations);
        }
    }

    private static void ValidateRate(MappingRule rule, int n, List<string> violations)
    {
        switch (rule.RateMode)
        {
            case RateMode.Fixed:
                if (rule.RateValue is null)
                {
                    violations.Add($"règle #{n} : taux fixe attendu mais aucune valeur de taux n'est renseignée");
                }
                else if (rule.RateValue < 0m)
                {
                    violations.Add($"règle #{n} : taux négatif ({rule.RateValue}) interdit");
                }

                break;

            case RateMode.ComputedFromSource:
                if (rule.RateValue is not null)
                {
                    violations.Add(
                        $"règle #{n} : taux calculé depuis la source mais une valeur fixe ({rule.RateValue}) est renseignée — incohérent");
                }

                break;

            default:
                violations.Add($"règle #{n} : mode de taux inconnu ({(int)rule.RateMode})");
                break;
        }
    }
}
