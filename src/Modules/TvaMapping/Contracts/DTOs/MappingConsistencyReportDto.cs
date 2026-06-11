namespace Liakont.Modules.TvaMapping.Contracts.DTOs;

/// <summary>
/// Rapport de cohérence du paramétrage de mapping TVA d'un tenant en lecture (lot FIX03) : symétrique du
/// rapport de couverture (TVA03). Liste les RÈGLES qui ne pourront jamais s'appliquer (part non
/// consultée, code jamais observé) — signal d'avertissement avant validation, jamais un blocage inventé
/// (CLAUDE.md n°3). Lecture tenant-scopée (CLAUDE.md n°9/17).
/// </summary>
public record MappingConsistencyReportDto
{
    /// <summary><c>true</c> si une table de mapping est configurée pour le tenant.</summary>
    public required bool IsTableConfigured { get; init; }

    /// <summary>Règles mortes détectées (vide si aucune incohérence).</summary>
    public required IReadOnlyList<DeadMappingRuleDto> DeadRules { get; init; }
}
