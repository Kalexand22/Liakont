namespace Liakont.Modules.TvaMapping.Domain.ConsistencyDetection;

using Liakont.Modules.TvaMapping.Domain.Entities;

/// <summary>
/// Vue minimale d'une règle de mapping pour le contrôle de cohérence (lot FIX03) : sa clé (code régime +
/// part) et son libellé. L'analyse n'a pas besoin de la catégorie / du taux : elle vérifie seulement que
/// la règle PEUT s'appliquer (part consultée, code observé).
/// </summary>
public sealed record MappingRuleConsistencyView
{
    /// <summary>Code du régime source de la règle.</summary>
    public required string SourceRegimeCode { get; init; }

    /// <summary>Part de la règle (Adjudication / Frais / Autre).</summary>
    public required MappingPart Part { get; init; }

    /// <summary>Libellé lisible de la règle, facultatif.</summary>
    public string? Label { get; init; }
}
