namespace Liakont.Agent.Contracts;

using System;

/// <summary>
/// Arrondi canonique des montants du contrat : arrondi commercial « half-up » à 2 décimales
/// (CLAUDE.md n°1 — half-up = <see cref="MidpointRounding.AwayFromZero"/>, valable aussi pour les
/// montants négatifs des avoirs). C'est un UTILITAIRE DE CONTRAT, pas de la logique métier : il
/// vit dans l'assembly partagé pour que l'agent (net48) et la plateforme (.NET 10) arrondissent à
/// l'IDENTIQUE — même raison d'être que le sérialiseur canonique de PIV02. Les DTOs eux-mêmes ne
/// calculent rien (F01-F02 §3.7 règle 2). La conversion d'un montant source float→decimal et sa
/// sanitisation (NaN / Infinity / hors-plage decimal → erreur typée SourceSchemaException, F01-F02
/// R7) appartiennent à l'ADAPTATEUR (ADR-0004 D3-7), qui appelle ensuite <see cref="RoundAmount"/>
/// sur le decimal validé ; l'original brut reste dans SourceData.
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
}
