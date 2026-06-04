namespace Liakont.Modules.TvaMapping.Domain.CoverageDetection;

/// <summary>
/// Rapport de couverture du mapping TVA d'un tenant (item TVA03, F03 §4.3) : croisement des régimes
/// source observés avec la table de mapping du tenant. Distingue les régimes couverts des régimes
/// absents (avec leurs occurrences) et porte le verdict (complet / incomplet). Produit par
/// <see cref="MappingCoverageAnalyzer"/> ; exposé à la console (« Complétez la table des régimes de
/// TVA », WEB07) et exploitable par le pipeline. Lecture tenant-scopée uniquement (CLAUDE.md n°9/17).
/// </summary>
public sealed class MappingCoverageReport
{
    /// <summary><c>true</c> si une table de mapping est configurée pour le tenant ; sinon tous les régimes sont absents.</summary>
    public required bool IsTableConfigured { get; init; }

    /// <summary>Version de la table confrontée, ou <c>null</c> si aucune table n'est configurée.</summary>
    public string? MappingVersion { get; init; }

    /// <summary>État de validation humaine de la table (« NON VALIDÉE » = <c>false</c>, INV-006) ; <c>false</c> si aucune table.</summary>
    public required bool IsTableValidated { get; init; }

    /// <summary>Régimes source observés couverts par au moins une règle de la table.</summary>
    public required IReadOnlyList<ObservedSourceRegime> CoveredRegimes { get; init; }

    /// <summary>Régimes source observés absents de la table (à mapper avant tout envoi).</summary>
    public required IReadOnlyList<ObservedSourceRegime> AbsentRegimes { get; init; }

    /// <summary>Verdict global : <see cref="MappingCoverageVerdict.Incomplete"/> dès qu'un régime est absent.</summary>
    public required MappingCoverageVerdict Verdict { get; init; }
}
