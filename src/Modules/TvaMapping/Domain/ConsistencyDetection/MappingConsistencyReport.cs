namespace Liakont.Modules.TvaMapping.Domain.ConsistencyDetection;

/// <summary>
/// Rapport de cohérence du paramétrage de mapping TVA d'un tenant (lot FIX03) : symétrique du rapport de
/// couverture (TVA03). Là où la couverture liste les régimes OBSERVÉS non mappés, la cohérence liste les
/// RÈGLES qui ne pourront jamais s'appliquer (part non consultée, code jamais observé). C'est un signal
/// d'avertissement avant validation, jamais un blocage inventé (CLAUDE.md n°3).
/// </summary>
public sealed class MappingConsistencyReport
{
    /// <summary><c>true</c> si une table de mapping est configurée pour le tenant.</summary>
    public required bool IsTableConfigured { get; init; }

    /// <summary>Règles mortes détectées (vide si aucune incohérence).</summary>
    public required IReadOnlyList<DeadMappingRule> DeadRules { get; init; }

    /// <summary><c>true</c> dès qu'au moins une règle morte est détectée.</summary>
    public bool HasDeadRules => DeadRules.Count > 0;
}
