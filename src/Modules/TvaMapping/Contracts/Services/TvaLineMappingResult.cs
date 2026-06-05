namespace Liakont.Modules.TvaMapping.Contracts.Services;

/// <summary>
/// Résultat du mapping d'une part de ligne : soit MAPPÉ ({catégorie UNCL5305, taux, VATEX}), soit BLOQUÉ
/// (motif opérateur). Conformément à <c>defaultBehavior=block</c> (F03 §4.1), un régime non couvert est
/// bloqué — aucune catégorie n'est devinée (CLAUDE.md n°2/3).
/// </summary>
public sealed record TvaLineMappingResult
{
    /// <summary>Code régime source de la requête (écho).</summary>
    public required string SourceRegimeCode { get; init; }

    /// <summary>Référence de la ligne d'origine (écho de la requête), facultative.</summary>
    public string? LineRef { get; init; }

    /// <summary><c>true</c> si une règle a été appliquée ; <c>false</c> si bloqué.</summary>
    public required bool IsMapped { get; init; }

    /// <summary>Catégorie de TVA produite (nom UNCL5305, ex. « S », « E ») ; <c>null</c> si bloqué.</summary>
    public string? Category { get; init; }

    /// <summary>Taux fixe (pourcentage, <c>decimal</c>) ; <c>null</c> si taux calculé en aval ou bloqué.</summary>
    public decimal? Rate { get; init; }

    /// <summary>Code VATEX produit (motif d'exonération) ; <c>null</c> si non applicable ou bloqué.</summary>
    public string? Vatex { get; init; }

    /// <summary>Motif de blocage (message opérateur français avec action) ; renseigné ssi <see cref="IsMapped"/> est <c>false</c>.</summary>
    public string? BlockReason { get; init; }
}
