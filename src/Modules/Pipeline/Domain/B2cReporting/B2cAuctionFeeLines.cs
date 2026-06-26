namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using System.Collections.Generic;
using System.Linq;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Source de vérité UNIQUE de la partition des lignes d'un bordereau d'enchères entre ADJUDICATION et HONORAIRE
/// ACHETEUR, depuis que l'honoraire acheteur est porté en LIGNE (rôle <see cref="PivotLineRole.BuyerFee"/>) et non
/// plus dans un side-channel hors-lignes (F03 §2.3 amendement 2026-06-26, BUG-17 volet b). Toute la machinerie B2C
/// (markings marge/taxable/export/ordinaire, découverte, aiguillage, jobs agrégés B4) doit distinguer la commission
/// acheteur de l'adjudication SANS la recompter — d'où ce helper partagé, jamais une copie locale (un oubli =
/// double-comptage ou sous-déclaration silencieuse, P1). L'honoraire VENDEUR reste hors-lignes
/// (<see cref="PivotDocumentDto.SellerFees"/>, décompte BV) — il n'est pas sur le bordereau de l'acheteur.
/// </summary>
public static class B2cAuctionFeeLines
{
    /// <summary>Vrai si la ligne est un HONORAIRE acheteur (rôle <see cref="PivotLineRole.BuyerFee"/>).</summary>
    public static bool IsBuyerFee(PivotLineDto line) => line.Role == PivotLineRole.BuyerFee;

    /// <summary>Les lignes d'HONORAIRE acheteur (commission) du document — jambe acheteur de la marge / du prix total.</summary>
    public static IEnumerable<PivotLineDto> BuyerFeeLines(PivotDocumentDto pivot) => pivot.Lines.Where(IsBuyerFee);

    /// <summary>
    /// Les lignes D'ADJUDICATION (toute ligne NON-honoraire : adjudication d'un lot ou ligne ordinaire). C'est le
    /// COMPLÉMENT strict de <see cref="BuyerFeeLines"/> — l'union des deux = toutes les lignes, sans recouvrement.
    /// </summary>
    public static IEnumerable<PivotLineDto> AdjudicationLines(PivotDocumentDto pivot) => pivot.Lines.Where(line => !IsBuyerFee(line));

    /// <summary>
    /// Vrai si le document porte des FRAIS d'enchères (discriminant « bordereau d'enchères ») : un honoraire acheteur
    /// (ligne au rôle <see cref="PivotLineRole.BuyerFee"/>) OU un honoraire vendeur (<see cref="PivotDocumentDto.SellerFees"/>).
    /// Remplace l'ancien test <c>BuyerFees.Count &gt; 0 || SellerFees.Count &gt; 0</c> (l'honoraire acheteur étant
    /// désormais une ligne). Pré-filtre / aiguillage partagé (B2C agrégé vs voie document / document ordinaire).
    /// </summary>
    public static bool HasAuctionFees(PivotDocumentDto pivot) =>
        ((pivot.SellerFees?.Count ?? 0) > 0) || pivot.Lines.Any(IsBuyerFee);
}
