namespace Liakont.Modules.TvaMapping.Domain.ConsistencyDetection;

using Liakont.Modules.TvaMapping.Domain.Entities;

/// <summary>
/// Règle de mapping détectée comme « morte » par le contrôle de cohérence (lot FIX03) : elle ne pourra
/// jamais s'appliquer aux documents du tenant, pour un ou plusieurs <see cref="DeadRuleReason"/>.
/// </summary>
public sealed record DeadMappingRule
{
    /// <summary>Code du régime source de la règle morte.</summary>
    public required string SourceRegimeCode { get; init; }

    /// <summary>Part de la règle morte.</summary>
    public required MappingPart Part { get; init; }

    /// <summary>Libellé lisible de la règle, facultatif.</summary>
    public string? Label { get; init; }

    /// <summary>Motifs (au moins un) pour lesquels la règle est morte.</summary>
    public required IReadOnlyList<DeadRuleReason> Reasons { get; init; }
}
