namespace Liakont.Modules.Payments.Domain.Entities;

using System;

/// <summary>
/// Garde-fou d'INTÉGRITÉ DE STOCKAGE des montants et taux du module Payments (CLAUDE.md n°1/4 — items
/// fiscaux). Une colonne <c>numeric</c> tronque silencieusement toute valeur dépassant son échelle, ce qui
/// altérerait un montant audité sans erreur visible : ce garde-fou rejette la valeur AVANT persistance. Il
/// ne juge pas la CORRECTION fiscale (calcul d'agrégation = PIP03) — uniquement le stockage SANS PERTE.
/// </summary>
internal static class MonetaryScale
{
    /// <summary>Rejette un montant à plus de 2 décimales (colonne <c>numeric(18,2)</c>).</summary>
    public static void RequireAmount(decimal value, string paramName)
    {
        if (decimal.Round(value, 2) != value)
        {
            throw new ArgumentException(
                $"Le montant '{paramName}' ({value}) dépasse 2 décimales : la colonne numeric(18,2) le tronquerait silencieusement, altérant un montant audité (CLAUDE.md n°4).",
                paramName);
        }
    }

    /// <summary>Rejette un taux à plus de 4 décimales (colonne <c>numeric(6,4)</c> — couvre 20.0000 comme 0.2000).</summary>
    public static void RequireRate(decimal value, string paramName)
    {
        if (decimal.Round(value, 4) != value)
        {
            throw new ArgumentException(
                $"Le taux '{paramName}' ({value}) dépasse 4 décimales : la colonne numeric(6,4) le tronquerait silencieusement (intégrité de stockage, CLAUDE.md n°4).",
                paramName);
        }
    }
}
