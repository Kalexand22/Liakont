namespace Liakont.Modules.TvaMapping.Contracts.DTOs;

/// <summary>
/// Rapport de couverture du mapping TVA d'un tenant en lecture (item TVA03, F03 §4.3) : croisement
/// des régimes source observés (push de l'agent — PIV04) avec la table de mapping du tenant. Distingue
/// les régimes couverts des régimes absents et porte le verdict. Consommé par la console (« Complétez
/// la table des régimes de TVA », WEB07) et par le pipeline. Lecture tenant-scopée (CLAUDE.md n°9/17).
/// </summary>
public record MappingCoverageReportDto
{
    /// <summary><c>true</c> si une table de mapping est configurée pour le tenant ; sinon tous les régimes sont absents.</summary>
    public required bool IsTableConfigured { get; init; }

    /// <summary>Version de la table confrontée, <c>null</c> si aucune table n'est configurée.</summary>
    public string? MappingVersion { get; init; }

    /// <summary>État de validation humaine de la table (« NON VALIDÉE » = <c>false</c>) ; <c>false</c> si aucune table.</summary>
    public required bool IsTableValidated { get; init; }

    /// <summary>Verdict global, exposé par son nom : <c>Complete</c> / <c>Incomplete</c>.</summary>
    public required string Verdict { get; init; }

    /// <summary>Régimes source observés couverts par la table.</summary>
    public required IReadOnlyList<RegimeCoverageDto> CoveredRegimes { get; init; }

    /// <summary>Régimes source observés absents de la table (à mapper avant tout envoi).</summary>
    public required IReadOnlyList<RegimeCoverageDto> AbsentRegimes { get; init; }
}
