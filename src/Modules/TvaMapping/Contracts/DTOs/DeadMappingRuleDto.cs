namespace Liakont.Modules.TvaMapping.Contracts.DTOs;

/// <summary>
/// Règle de mapping détectée comme « morte » par le contrôle de cohérence (lot FIX03) en lecture :
/// elle ne pourra jamais s'appliquer aux documents du tenant. Les motifs sont exposés par leur nom
/// (<c>PartNotConsulted</c> / <c>RegimeNeverObserved</c>) — l'UI les traduit en messages opérateur.
/// </summary>
public record DeadMappingRuleDto
{
    /// <summary>Code du régime source de la règle morte.</summary>
    public required string SourceRegimeCode { get; init; }

    /// <summary>Part de la règle morte (<c>Adjudication</c> / <c>Frais</c> / <c>Autre</c>).</summary>
    public required string Part { get; init; }

    /// <summary>Libellé lisible de la règle, facultatif.</summary>
    public string? Label { get; init; }

    /// <summary>Motifs (au moins un) pour lesquels la règle est morte, exposés par leur nom.</summary>
    public required IReadOnlyList<string> Reasons { get; init; }
}
