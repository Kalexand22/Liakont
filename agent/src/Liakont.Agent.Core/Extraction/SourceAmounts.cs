namespace Liakont.Agent.Core.Extraction;

using System;
using Liakont.Agent.Contracts;

/// <summary>
/// Conversion GARDÉE d'un montant source flottant (legacy) en <c>decimal</c> + sanitisation
/// NaN / Infini / hors-plage en <see cref="SourceSchemaException"/> typée (ADR-0004 D3-7, F01-F02 R7,
/// CLAUDE.md n°1). Réutilisable par tout adaptateur dont la source stocke des <c>float</c> (ex. DemoErpB).
/// L'arrondi commercial half-up lui-même vit dans le contrat partagé (<see cref="PivotRounding"/>) pour
/// que l'agent et la plateforme arrondissent à l'identique ; l'original brut est conservé en SourceData.
/// </summary>
public static class SourceAmounts
{
    /// <summary>
    /// Convertit un montant flottant source en <c>decimal</c> arrondi à 2 décimales (half-up).
    /// Lève <see cref="SourceSchemaException"/> si la valeur est NaN, infinie ou hors de la plage decimal
    /// — jamais arrondi à l'aveugle (ADR-0004 D3-7).
    /// </summary>
    /// <param name="raw">Le montant brut (flottant source).</param>
    /// <param name="field">Le nom du champ source, inclus dans le message opérateur.</param>
    /// <returns>Le montant en <c>decimal</c> arrondi à 2 décimales (half-up).</returns>
    public static decimal RoundAmount(double raw, string field) => PivotRounding.RoundAmount(ToDecimal(raw, field));

    /// <summary>
    /// Convertit un flottant NON-montant (taux, quantité) en <c>decimal</c> SANS arrondi supplémentaire.
    /// Lève <see cref="SourceSchemaException"/> si la valeur est NaN, infinie ou hors de la plage decimal.
    /// </summary>
    /// <param name="raw">La valeur brute (flottant source).</param>
    /// <param name="field">Le nom du champ source, inclus dans le message opérateur.</param>
    /// <returns>La valeur convertie en <c>decimal</c> sans arrondi.</returns>
    public static decimal ToDecimal(double raw, string field)
    {
        if (double.IsNaN(raw) || double.IsInfinity(raw))
        {
            throw new SourceSchemaException(
                $"Valeur source illisible pour le champ « {field} » (NaN/Infini) : valeur brute « {raw} ». "
                + "Document bloqué, jamais arrondi à l'aveugle (ADR-0004 D3-7). Vérifiez l'extraction des données source.");
        }

        try
        {
            return (decimal)raw;
        }
        catch (OverflowException ex)
        {
            throw new SourceSchemaException(
                $"Valeur source hors de la plage decimal pour le champ « {field} » : valeur brute « {raw} ». "
                + "Document bloqué (ADR-0004 D3-7). Vérifiez l'extraction des données source.",
                ex);
        }
    }
}
