namespace Liakont.Modules.TvaMapping.Domain.CoverageDetection;

/// <summary>
/// Détection proactive des régimes de TVA source non mappés (item TVA03, F03 §4.3). Croise les
/// régimes source OBSERVÉS (métadonnées de push de l'agent, persistées par tenant — PIV04) avec les
/// codes couverts par la table de mapping du tenant, et produit un <see cref="MappingCoverageReport"/>.
///
/// PUR et SANS ÉTAT (comme <c>TvaMapper</c>, INV-010) : ne consulte QUE ses arguments — aucune source
/// externe, aucune règle fiscale inventée (CLAUDE.md n°2). La couverture est évaluée au grain du CODE
/// de régime : un code est « couvert » dès qu'au moins une règle de la table le référence (quelle que
/// soit la part). Les métadonnées observées ne portent que le code, le libellé et les occurrences —
/// pas la part — d'où une couverture au grain du code (limite assumée, MODULE.md) ; le contrôle fin
/// par (code, part) reste celui du moteur à l'exécution (TVA02, INV-007). La comparaison des codes est
/// EXACTE (<see cref="System.StringComparer.Ordinal"/>), cohérente avec le matching du moteur
/// (INV-011) : un code « couvert » mais de casse différente serait bloqué à l'exécution — le signaler
/// absent évite une fausse impression de couverture (INV-012).
/// </summary>
public static class MappingCoverageAnalyzer
{
    /// <summary>
    /// Croise les régimes source observés d'un tenant avec sa table de mapping.
    /// </summary>
    /// <param name="observedRegimes">Régimes source observés du tenant (peut être vide).</param>
    /// <param name="table">Vue de la table de mapping du tenant, ou <c>null</c> si aucune table n'est configurée.</param>
    /// <returns>Le rapport de couverture (régimes couverts / absents + verdict).</returns>
    public static MappingCoverageReport Analyze(
        IReadOnlyList<ObservedSourceRegime> observedRegimes,
        MappingTableSummary? table)
    {
        ArgumentNullException.ThrowIfNull(observedRegimes);

        var mappedCodes = new HashSet<string>(
            table?.MappedRegimeCodes ?? Array.Empty<string>(),
            StringComparer.Ordinal);

        var covered = new List<ObservedSourceRegime>();
        var absent = new List<ObservedSourceRegime>();
        foreach (var regime in observedRegimes)
        {
            if (mappedCodes.Contains(regime.Code))
            {
                covered.Add(regime);
            }
            else
            {
                absent.Add(regime);
            }
        }

        return new MappingCoverageReport
        {
            IsTableConfigured = table is not null,
            MappingVersion = table?.MappingVersion,
            IsTableValidated = table?.IsValidated ?? false,
            CoveredRegimes = covered,
            AbsentRegimes = absent,
            Verdict = absent.Count == 0
                ? MappingCoverageVerdict.Complete
                : MappingCoverageVerdict.Incomplete,
        };
    }
}
