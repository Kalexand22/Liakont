namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Dérivation PURE (sans I/O) du marqueur de DÉCLARATION B2C d'un document ORDINAIRE taxable (flux 10.3, F03
/// §2.9) — une vente ou une prestation que l'OVV facture DIRECTEMENT, HORS mécanisme d'enchères opaque : facture
/// client (livraison de biens) ou note d'honoraires d'inventaire (prestation de services). Posé par la PLATEFORME
/// sur <see cref="PivotDocumentDto.IsB2cReportingDeclaration"/>, DÉRIVÉ au read-time sur le pivot ENRICHI par le
/// mapping TVA validé (catégorie par ligne), jamais porté par l'agent ni persisté (pattern <see cref="B2cMarginMarking"/>).
///
/// <para>SYMÉTRIQUE de <see cref="B2cTaxableMarking"/> (même flux 10.3, même canal B2C, même critère de lignes
/// taxables), avec UN SEUL discriminant inversé : le document ORDINAIRE <b>ne porte AUCUN frais d'enchères</b>
/// (ni acheteur ni vendeur). Les frais sont la signature du bordereau d'enchères (§2.7/§2.8) ; leur ABSENCE
/// distingue la facture/note ordinaire. La catégorie de transaction TT-81 n'est PAS décidée ici (le marquage est
/// agnostique bien/service) : elle est dérivée de <see cref="PivotDocumentDto.OperationCategory"/> côté job
/// (<c>LivraisonBiens → TLB1</c>, <c>PrestationServices → TPS1</c> — G1.68).</para>
///
/// <para><b>Critère SOURCÉ (F03 §2.9), AUCUNE règle inventée (CLAUDE.md n°2) :</b></para>
/// <list type="number">
///   <item><b>AUCUN frais</b> d'enchères (<see cref="PivotDocumentDto.BuyerFees"/>/<see cref="PivotDocumentDto.SellerFees"/>
///   vides) — discriminant « document ordinaire ≠ bordereau d'enchères ». C'est l'inverse exact du critère (2) de
///   <see cref="B2cTaxableMarking"/> : marge/taxable/export enchères EXIGENT des frais, l'ordinaire les EXCLUT
///   (partition nette, aucun double marquage).</item>
///   <item><b>Régime du prix total taxable</b> : TVA distincte au grain document
///   (<see cref="PivotTotalsDto.TotalTax"/> &gt; 0) ET toutes les lignes mappées à une catégorie taxable à taux
///   positif (<c>S</c>/<c>AA</c>/<c>AAA</c>, TABLE VALIDÉE — F03 §2.1/§3). Une ligne exonérée / hors champ mêlée →
///   NON marquée (fail-closed).</item>
///   <item><b>B2C</b> : acheteur NON professionnel (<see cref="B2cBuyerClassification.IsNonProfessional"/>,
///   prédicat PARTAGÉ — invariant d'aiguillage). Un client à SIREN relève du B2B (e-invoicing), jamais de
///   l'e-reporting B2C — canal piloté par le STATUT DU TIERS (F03 §2.9).</item>
/// </list>
/// </summary>
public static class B2cPlainTaxableMarking
{
    /// <summary>
    /// Catégories UNCL5305 taxables à TAUX POSITIF (F03 §2.1) qualifiant une opération « soumise à la TVA »
    /// (TLB1/TPS1, G1.68). Identique à <see cref="B2cTaxableMarking"/> : <c>S</c>/<c>AA</c>/<c>AAA</c>.
    /// </summary>
    private static readonly HashSet<VatCategory> TaxableCategories = new()
    {
        VatCategory.S,
        VatCategory.AA,
        VatCategory.AAA,
    };

    /// <summary>
    /// Vrai si <paramref name="enrichedPivot"/> (DÉJÀ enrichi par le mapping TVA validé) qualifie une déclaration
    /// B2C d'un document ORDINAIRE taxable (flux 10.3, TLB1/TPS1, F03 §2.9). Fail-closed : tout signal manquant ou
    /// ambigu → <c>false</c> (jamais marqué à tort).
    /// </summary>
    /// <param name="enrichedPivot">Le pivot enrichi par le mapping TVA (lignes portant catégorie/VATEX).</param>
    /// <returns><c>true</c> si déclaration B2C ordinaire taxable (à marquer), <c>false</c> sinon.</returns>
    public static bool IsPlainTaxableB2cDeclaration(PivotDocumentDto enrichedPivot)
    {
        if (enrichedPivot is null)
        {
            return false;
        }

        // (1) AUCUN frais d'enchères — discriminant « document ordinaire » (inverse du taxable/marge enchères).
        bool hasFees = B2cAuctionFeeLines.HasAuctionFees(enrichedPivot);
        if (hasFees)
        {
            return false;
        }

        // (2a) TVA distincte au grain document (régime du prix total taxable, F03 §2.9).
        if (enrichedPivot.Totals.TotalTax <= 0m)
        {
            return false;
        }

        // (3) Acheteur non professionnel (B2C, F03 §2.9) — prédicat partagé (invariant d'aiguillage).
        if (!B2cBuyerClassification.IsNonProfessional(enrichedPivot.Customer))
        {
            return false;
        }

        // (2b) Toutes les lignes taxables à taux positif (S/AA/AAA), issu de la TABLE VALIDÉE (fail-closed).
        return AllLinesTaxable(enrichedPivot.Lines);
    }

    /// <summary>
    /// Vrai si TOUTES les lignes (au moins une) sont mappées à une catégorie taxable à taux positif
    /// (<c>S</c>/<c>AA</c>/<c>AAA</c>). Une ligne exonérée / hors champ mêlée rend le document ambigu → non marqué
    /// (fail-closed, F03 §2.9).
    /// </summary>
    private static bool AllLinesTaxable(IReadOnlyList<PivotLineDto> lines)
    {
        if (lines.Count == 0)
        {
            return false;
        }

        foreach (var line in lines)
        {
            if (line.Taxes.Count != 1)
            {
                return false;
            }

            var category = line.Taxes[0].CategoryCode;
            if (category is null || !TaxableCategories.Contains(category.Value))
            {
                return false;
            }
        }

        return true;
    }
}
