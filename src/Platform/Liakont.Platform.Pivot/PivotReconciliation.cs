namespace Liakont.Platform.Pivot;

using System;
using System.Linq;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Réconciliation des totaux d'un document pivot — règle BR-CO-13 (EN 16931, F04 §3.3), SOURCE UNIQUE pour
/// que la validation bloquante (<c>LineTotalsRule</c>, module Validation) et l'affichage console du contrôle de
/// cohérence (<c>DocumentLineProjection</c>, FIX205) ne puissent JAMAIS diverger : un seul endroit porte la
/// formule (quelles charges/remises s'ajoutent/retranchent, quel arrondi). Pur, sans état, aucun montant
/// recalculé au-delà de la somme arrondie (CLAUDE.md n°1).
/// <para>
/// Vit dans un assembly PLATEFORME-SEUL (et non plus dans <c>Liakont.Agent.Contracts</c>, paquet publiable
/// consommé par l'agent net48) : l'agent n'a AUCUNE logique métier (ADR-0005 décision 3, CLAUDE.md n°6).
/// La frontière agent reste préservée tout en gardant cette formule source-unique côté plateforme (RDL12).
/// </para>
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
        ArgumentNullException.ThrowIfNull(document);

        // Remises/charges de niveau document, en HT : IsCharge = true ajoute (BG-21), false retranche (BG-20).
        var documentChargeNet = document.DocumentCharges.Sum(charge => charge.IsCharge ? charge.Amount : -charge.Amount);
        return PivotRounding.RoundAmount(document.Lines.Sum(line => line.NetAmount) + documentChargeNet);
    }
}
