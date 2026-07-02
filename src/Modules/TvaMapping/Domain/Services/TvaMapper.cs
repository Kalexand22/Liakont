namespace Liakont.Modules.TvaMapping.Domain.Services;

using Liakont.Modules.TvaMapping.Domain.Entities;
using Liakont.Modules.TvaMapping.Domain.Mapping;

/// <summary>
/// Moteur de mapping TVA (item TVA02, F03 §4) — cœur de valeur fiscale du produit. Applique la table
/// VALIDÉE du tenant à une <see cref="MappingRequest"/> (code régime source brut + part + flags) et
/// produit un <see cref="MappingResult"/> : soit le triplet normalisé {catégorie UNCL5305, taux,
/// VATEX} accompagné d'une <see cref="MappingTrace"/> d'audit, soit un blocage.
/// </summary>
/// <remarks>
/// Service de domaine PUR et SANS ÉTAT : il ne lit QUE la table passée en argument — aucune donnée
/// partagée entre tenants, aucune source de vérité globale (isolation tenant structurelle,
/// CLAUDE.md n°9/INV-008). Il n'invente AUCUNE règle fiscale (CLAUDE.md n°2) : un régime non couvert,
/// ou une règle dont les flags requis ne sont pas satisfaits, déclenche le comportement par défaut
/// <c>block</c> (F03 §4.1, INV-007) plutôt qu'un mapping deviné. La validation de cohérence de la
/// table (catégories, E→VATEX, taux) est garantie en amont par <see cref="MappingTableValidator"/>
/// (à la création ET au chargement) ; le moteur n'a donc pas à la revérifier.
/// </remarks>
public static class TvaMapper
{
    /// <summary>
    /// Mappe une part de ligne contre la table du tenant. Ne lève pas pour un régime non couvert :
    /// retourne un <see cref="MappingResult"/> bloqué (le blocage est un résultat métier, pas une
    /// erreur technique).
    /// </summary>
    /// <param name="table">Table de mapping du tenant (déjà validée structurellement).</param>
    /// <param name="request">Entrée : code régime source brut, part, flags effectifs du document.</param>
    /// <param name="mappedAt">Horodatage (UTC) du mapping, fourni par l'appelant (déterminisme + audit).</param>
    public static MappingResult Map(MappingTable table, MappingRequest request, DateTimeOffset mappedAt)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(request);

        // Au plus UNE règle par (code régime source, part) — unicité garantie par le validateur et la
        // contrainte de base (INV-003). La première correspondance est donc l'unique ; les flags ne
        // créent pas de seconde règle, ils RESTREIGNENT celle-ci.
        MappingRule? rule = null;
        var ordinal = 0;
        for (var i = 0; i < table.Rules.Count; i++)
        {
            var candidate = table.Rules[i];
            if (string.Equals(candidate.SourceRegimeCode, request.SourceRegimeCode, StringComparison.Ordinal)
                && candidate.Part == request.Part)
            {
                rule = candidate;
                ordinal = i + 1;
                break;
            }
        }

        if (rule is null)
        {
            // Régime non couvert → comportement par défaut. Seul `block` est sourcé (F03 §4.1) ; toute
            // autre issue exigerait une décision tracée dans docs/conception/ (CLAUDE.md n°2).
            return MappingResult.Blocked(
                $"Régime de TVA source « {request.SourceRegimeCode} » (part « {request.Part} ») absent de la " +
                $"table de mapping du tenant (version « {table.MappingVersion} ») : document bloqué " +
                "— aucune catégorie n'est devinée. Action opérateur : " +
                "ajoutez une règle pour ce régime dans la console (Paramétrage › TVA), puis revalidez " +
                "la table avant tout envoi.");
        }

        if (!FlagsSatisfied(rule.SourceFlags, request.SourceFlags))
        {
            return MappingResult.Blocked(
                $"Régime de TVA source « {request.SourceRegimeCode} » (part « {request.Part} ») : la règle " +
                $"de la table (version « {table.MappingVersion} ») existe mais ses conditions de flags source " +
                $"ne sont pas satisfaites par le document (flags requis : {DescribeFlags(rule.SourceFlags)}). " +
                "Document bloqué — aucune catégorie n'est devinée. Action opérateur : vérifiez la " +
                "règle (flags source attendus) dans la console (Paramétrage › TVA), puis revalidez " +
                "la table.");
        }

        // Taux : figé pour Fixed ; pour ComputedFromSource il est résolu en aval (pipeline PIP01) à
        // partir des montants de la ligne (montant_tva / montant_ht — F03 §4.1), inconnus du moteur.
        var rate = rule.RateMode == RateMode.Fixed ? rule.RateValue : null;

        var trace = new MappingTrace
        {
            MappedAt = mappedAt,
            MappingVersion = table.MappingVersion,
            ValidatedBy = table.ValidatedBy,
            ValidatedDate = table.ValidatedDate,
            IsValidated = table.IsValidated,
            InputRegimeCode = request.SourceRegimeCode,
            Part = request.Part,
            RuleOrdinal = ordinal,
            RuleLabel = rule.Label,
            Category = rule.Category,
            RateMode = rule.RateMode,
            Rate = rate,
            Vatex = rule.Vatex,
        };

        return MappingResult.Mapped(rule.Category, rule.RateMode, rate, rule.Vatex, trace);
    }

    /// <summary>
    /// Vrai si tous les flags RESTRICTIFS de la règle sont satisfaits par les flags effectifs du
    /// document. Une règle sans flag s'applique inconditionnellement ; un document sans flag ne
    /// satisfait aucune condition de flag. Comparaison EXACTE (ordinale) : l'opérateur paramètre la
    /// valeur attendue exactement telle que la source l'émet — aucune normalisation devinée.
    /// </summary>
    private static bool FlagsSatisfied(
        IReadOnlyDictionary<string, string>? required,
        IReadOnlyDictionary<string, string>? actual)
    {
        if (required is null || required.Count == 0)
        {
            return true;
        }

        if (actual is null)
        {
            return false;
        }

        foreach (var (key, expected) in required)
        {
            if (!actual.TryGetValue(key, out var value) || !string.Equals(value, expected, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string DescribeFlags(IReadOnlyDictionary<string, string>? flags)
    {
        if (flags is null || flags.Count == 0)
        {
            return "aucun";
        }

        return string.Join(", ", flags.Select(kv => $"{kv.Key}={kv.Value}"));
    }
}
