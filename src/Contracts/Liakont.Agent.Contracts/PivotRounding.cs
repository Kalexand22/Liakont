namespace Liakont.Agent.Contracts;

using System;

/// <summary>
/// Arrondi canonique des montants du contrat : arrondi commercial « half-up » à 2 décimales
/// (CLAUDE.md n°1 — half-up = <see cref="MidpointRounding.AwayFromZero"/>, valable aussi pour les
/// montants négatifs des avoirs). C'est un UTILITAIRE DE CONTRAT, pas de la logique métier : il
/// vit dans l'assembly partagé pour que l'agent (net48) et la plateforme (.NET 10) arrondissent à
/// l'IDENTIQUE — même raison d'être que le sérialiseur canonique de PIV02. Les DTOs eux-mêmes ne
/// calculent rien (F01-F02 §3.7 règle 2) : l'arrondi float→decimal se fait à la frontière de
/// l'adaptateur (ADR-0004 D3-7), en appelant cet utilitaire, l'original restant dans SourceData.
/// </summary>
public static class PivotRounding
{
    /// <summary>Échelle des montants du contrat : 2 décimales.</summary>
    public const int AmountScale = 2;

    /// <summary>
    /// Arrondit un montant à <see cref="AmountScale"/> décimales en arrondi commercial half-up
    /// (away-from-zero). Exemples : 1,005 → 1,01 ; 1,004 → 1,00 ; −1,005 → −1,01.
    /// </summary>
    /// <param name="value">Le montant à arrondir.</param>
    /// <returns>Le montant arrondi à 2 décimales.</returns>
    public static decimal RoundAmount(decimal value) =>
        Math.Round(value, AmountScale, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Convertit un montant <see cref="double"/> issu d'une source legacy en <see cref="decimal"/>
    /// arrondi half-up à 2 décimales (ADR-0004 D3-7 : les bases legacy stockent des flottants sales
    /// comme 8,329999999999998). À utiliser à la frontière de l'adaptateur, l'original brut étant
    /// conservé dans SourceData.
    /// </summary>
    /// <param name="value">Le montant source en double.</param>
    /// <returns>Le montant en decimal arrondi à 2 décimales.</returns>
    public static decimal FromSourceDouble(double value) =>
        RoundAmount((decimal)value);
}
