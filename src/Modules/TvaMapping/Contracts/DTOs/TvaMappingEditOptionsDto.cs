namespace Liakont.Modules.TvaMapping.Contracts.DTOs;

/// <summary>
/// Listes FERMÉES proposées à l'édition d'une règle de mapping TVA (item TVA05 / WEB07b) : catégories
/// UNCL5305, parts, modes de taux et codes VATEX admis. Toutes les valeurs sont SOURCÉES (catégories
/// F03 §2.1, parts/modes de taux = énumérations de la spec, VATEX F03 §2.2) et dérivées des mêmes
/// sources que le moteur d'édition TVA05 — la console n'invente AUCUNE valeur fiscale et n'autorise
/// jamais la saisie libre sur ces champs (CLAUDE.md n°2). Vocabulaire statique (sans tenant).
/// </summary>
public sealed record TvaMappingEditOptionsDto
{
    /// <summary>Catégories de TVA admises (code UNCL5305 + libellé), F03 §2.1.</summary>
    public required IReadOnlyList<TvaMappingOptionDto> Categories { get; init; }

    /// <summary>Parts de ligne admises (Adjudication / Frais / Autre), F03 §4.1.</summary>
    public required IReadOnlyList<TvaMappingOptionDto> Parts { get; init; }

    /// <summary>Modes de taux admis (Fixed / ComputedFromSource), F03 §4.1.</summary>
    public required IReadOnlyList<TvaMappingOptionDto> RateModes { get; init; }

    /// <summary>Codes VATEX admis (code + usage), F03 §2.2.</summary>
    public required IReadOnlyList<TvaMappingOptionDto> VatexCodes { get; init; }
}
