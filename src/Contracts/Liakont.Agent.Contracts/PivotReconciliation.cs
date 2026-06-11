namespace Liakont.Agent.Contracts;

using System;
using System.Linq;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Réconciliation des totaux d'un document pivot — règle BR-CO-13 (EN 16931, F04 §3.3), SOURCE UNIQUE pour
/// que la validation bloquante (<c>LineTotalsRule</c>, module Validation) et l'affichage console du contrôle de
/// cohérence (<c>DocumentLineProjection</c>, FIX205) ne puissent JAMAIS diverger : un seul endroit porte la
/// formule (quelles charges/remises s'ajoutent/retranchent, quel arrondi). Pur, sans état, aucun montant
/// recalculé au-delà de la somme arrondie (CLAUDE.md n°1).
/// </summary>
public static class PivotReconciliation
{
    /// <summary>
    /// Total HT attendu (BT-109) = Σ lignes HT (BT-131) − remises de niveau document (BG-20)
    /// + charges de niveau document (BG-21), arrondi commercial half-up à 2 décimales
    /// (<see cref="PivotRounding.RoundAmount"/>). C'est la valeur à comparer à <c>Totals.TotalNet</c>
    /// (réconciliation BR-CO-13, tolérance 0).
    /// </summary>
    /// <param name="document">Le document pivot (jamais <c>null</c>).</param>
    public static decimal ExpectedNet(PivotDocumentDto document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        // Remises/charges de niveau document, en HT : IsCharge = true ajoute (BG-21), false retranche (BG-20).
        var documentChargeNet = document.DocumentCharges.Sum(charge => charge.IsCharge ? charge.Amount : -charge.Amount);
        return PivotRounding.RoundAmount(document.Lines.Sum(line => line.NetAmount) + documentChargeNet);
    }
}
