namespace Liakont.Modules.TvaMapping.Contracts.DTOs;

/// <summary>
/// Règle de mapping TVA en lecture (F03 §4.1). Les énumérations sont exposées par leur nom
/// (<see cref="Part"/>, <see cref="Category"/>, <see cref="RateMode"/>) ; <see cref="Category"/> est
/// le code UNCL5305 (S, AA, AAA, Z, E, AE, G, K, O). Le taux est un <c>decimal</c> exact, jamais un
/// flottant (CLAUDE.md n°1) ; <c>null</c> quand il est calculé depuis la source.
/// </summary>
public record MappingRuleDto
{
    /// <summary>Code du régime TVA dans le système source.</summary>
    public required string SourceRegimeCode { get; init; }

    /// <summary>Libellé lisible du régime source, facultatif.</summary>
    public string? Label { get; init; }

    /// <summary>Part concernée (<c>Adjudication</c> / <c>Frais</c> / <c>Autre</c>).</summary>
    public required string Part { get; init; }

    /// <summary>Conditions supplémentaires sur des flags source (nom → valeur attendue), facultatives.</summary>
    public IReadOnlyDictionary<string, string>? SourceFlags { get; init; }

    /// <summary>Catégorie de TVA produite (code UNCL5305).</summary>
    public required string Category { get; init; }

    /// <summary>Code VATEX (motif d'exonération), <c>null</c> si non applicable.</summary>
    public string? Vatex { get; init; }

    /// <summary>Note de paramétrage, facultative.</summary>
    public string? Note { get; init; }

    /// <summary>Mode de taux (<c>Fixed</c> / <c>ComputedFromSource</c>).</summary>
    public required string RateMode { get; init; }

    /// <summary>Taux fixe (pourcentage, <c>decimal</c>) ; <c>null</c> si calculé depuis la source.</summary>
    public decimal? RateValue { get; init; }
}
