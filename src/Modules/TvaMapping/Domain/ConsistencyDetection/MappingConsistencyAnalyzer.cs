namespace Liakont.Modules.TvaMapping.Domain.ConsistencyDetection;

using Liakont.Modules.TvaMapping.Domain.Entities;

/// <summary>
/// Contrôle de cohérence du paramétrage de mapping TVA (lot FIX03), symétrique de
/// <c>MappingCoverageAnalyzer</c> (TVA03). Là où la couverture part des régimes OBSERVÉS pour signaler
/// ceux qui ne sont pas mappés, la cohérence part des RÈGLES de la table pour signaler celles qui ne
/// pourront jamais s'appliquer :
/// <list type="bullet">
///   <item>part non consultée par les consommateurs du tenant (Adjudication — seul CHECK=Autre et B4=Frais consultent) ;</item>
///   <item>code régime jamais observé dans la source (faute de frappe probable).</item>
/// </list>
///
/// PUR et SANS ÉTAT (comme <c>MappingCoverageAnalyzer</c> / <c>TvaMapper</c>) : ne consulte QUE ses
/// arguments, aucune règle fiscale inventée (CLAUDE.md n°2). La comparaison des codes est EXACTE
/// (<see cref="System.StringComparer.Ordinal"/>), cohérente avec le moteur (INV-011) — un code de casse
/// différente ne matcherait pas à l'exécution, le signaler évite une fausse impression de cohérence.
/// Les motifs produits sont des SIGNAUX (avertissement), jamais des blocages (CLAUDE.md n°3).
/// </summary>
public static class MappingConsistencyAnalyzer
{
    /// <summary>
    /// Croise les règles d'une table avec les parts consultées et les régimes observés du tenant.
    /// </summary>
    /// <param name="rules">Règles de la table (peut être vide).</param>
    /// <param name="consultedParts">Parts réellement consultées par le pipeline du tenant (voir <see cref="ConsultedMappingParts"/>).</param>
    /// <param name="observedRegimeCodes">Codes régime observés dans la source du tenant (peut être vide).</param>
    /// <param name="tableConfigured"><c>true</c> si une table de mapping est configurée.</param>
    /// <returns>Le rapport de cohérence (règles mortes + motifs).</returns>
    public static MappingConsistencyReport Analyze(
        IReadOnlyList<MappingRuleConsistencyView> rules,
        IReadOnlySet<MappingPart> consultedParts,
        IReadOnlyCollection<string> observedRegimeCodes,
        bool tableConfigured)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(consultedParts);
        ArgumentNullException.ThrowIfNull(observedRegimeCodes);

        var observed = new HashSet<string>(observedRegimeCodes, StringComparer.Ordinal);

        // Le « jamais observé » n'est concluant que si des régimes ONT été observés : sur un tenant
        // vierge (aucun push d'agent), l'absence d'observation ne prouve rien — on ne le signale pas
        // (sinon TOUTES les règles seraient « mortes » tant qu'aucun document n'a été ingéré).
        var hasObservations = observed.Count > 0;

        var dead = new List<DeadMappingRule>();
        foreach (var rule in rules)
        {
            var reasons = new List<DeadRuleReason>();

            if (!consultedParts.Contains(rule.Part))
            {
                reasons.Add(DeadRuleReason.PartNotConsulted);
            }

            if (hasObservations && !observed.Contains(rule.SourceRegimeCode))
            {
                reasons.Add(DeadRuleReason.RegimeNeverObserved);
            }

            if (reasons.Count > 0)
            {
                dead.Add(new DeadMappingRule
                {
                    SourceRegimeCode = rule.SourceRegimeCode,
                    Part = rule.Part,
                    Label = rule.Label,
                    Reasons = reasons,
                });
            }
        }

        return new MappingConsistencyReport
        {
            IsTableConfigured = tableConfigured,
            DeadRules = dead,
        };
    }
}
