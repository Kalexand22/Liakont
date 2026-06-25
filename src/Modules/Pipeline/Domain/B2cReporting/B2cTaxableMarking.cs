namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Dérivation PURE (sans I/O) du marqueur de DÉCLARATION B2C TAXABLE (flux 10.3, enchères au RÉGIME DU PRIX
/// TOTAL — commettant ASSUJETTI) posé par la PLATEFORME sur
/// <see cref="PivotDocumentDto.IsB2cReportingDeclaration"/>. SYMÉTRIQUE de <see cref="B2cMarginMarking"/> (cas
/// marge, commettant non assujetti) : même flux 10.3, même canal (acheteur particulier → e-reporting B2C),
/// mais la catégorie de transaction TT-81 sera <c>TLB1</c> (livraison de biens taxable) au lieu de <c>TMA1</c>.
/// La distinction marge ↔ prix total est portée par <see cref="PivotTotalsDto.TotalTax"/> : <c>== 0</c> →
/// marge (art. 297 E) ; <c>&gt; 0</c> → prix total (TVA distincte). Comme le marqueur de marge, il est DÉRIVÉ
/// au read-time sur le pivot ENRICHI par le mapping TVA validé (catégorie par ligne), jamais porté par l'agent
/// ni persisté (pattern émetteur/TVA / <see cref="B2cMarginMarking"/>).
///
/// <para><b>Critère SOURCÉ (F03 §2.7), AUCUNE règle inventée (CLAUDE.md n°2) :</b></para>
/// <list type="number">
///   <item><b>Régime du prix total taxable</b> : TVA distincte au grain document
///   (<see cref="PivotTotalsDto.TotalTax"/> &gt; 0 — art. 297 E ne s'applique pas, F03 §2.7) ET toutes les
///   lignes (l'adjudication) mappées à une catégorie taxable à taux positif (<c>S</c>/<c>AA</c>/<c>AAA</c>,
///   issue de la TABLE VALIDÉE — F03 §2.1/§3). Une ligne exonérée / hors champ mêlée → NON marquée
///   (fail-closed, jamais <c>TLB1</c> à tort — F03 §2.7).</item>
///   <item><b>Frais d'enchères présents</b> (commission acheteur et/ou vendeur). C'est le discriminant
///   ENCHÈRES : ces frais (<see cref="PivotDocumentDto.BuyerFees"/>/<see cref="PivotDocumentDto.SellerFees"/>)
///   sont propres au bordereau d'enchères — une facture B2C ORDINAIRE (taxable, sans frais) n'est JAMAIS happée
///   vers le job agrégé, elle suit sa voie document nominale (aucune régression du B2C générique). En pratique
///   un bordereau d'enchères porte toujours des honoraires acheteur (frais de vente).</item>
///   <item><b>B2C</b> : acheteur NON professionnel (<see cref="B2cBuyerClassification.IsNonProfessional"/>,
///   prédicat PARTAGÉ avec la marge — invariant d'aiguillage). Un acheteur professionnel (SIREN, n° TVA, indice
///   société) relève du B2B (e-invoicing), jamais de l'e-reporting B2C — canal piloté par le STATUT DU TIERS,
///   jamais la nature du bien (F03 §2.7).</item>
/// </list>
/// </summary>
public static class B2cTaxableMarking
{
    /// <summary>
    /// Catégories UNCL5305 taxables à TAUX POSITIF (F03 §2.1) qualifiant une livraison « soumise à la TVA »
    /// (TLB1, G1.68). <c>Z</c> (taux zéro), <c>E</c>/<c>AE</c>/<c>G</c>/<c>K</c>/<c>O</c> (TVA nulle) sont exclus —
    /// au demeurant déjà écartés par le critère <see cref="PivotTotalsDto.TotalTax"/> &gt; 0.
    /// </summary>
    private static readonly HashSet<VatCategory> TaxableCategories = new()
    {
        VatCategory.S,
        VatCategory.AA,
        VatCategory.AAA,
    };

    /// <summary>
    /// Vrai si <paramref name="enrichedPivot"/> (DÉJÀ enrichi par le mapping TVA validé : catégorie posée sur
    /// les lignes) qualifie une déclaration B2C TAXABLE au régime du prix total (flux 10.3, catégorie TLB1).
    /// Fail-closed : tout signal manquant ou ambigu → <c>false</c> (jamais marqué à tort).
    /// </summary>
    /// <param name="enrichedPivot">Le pivot enrichi par le mapping TVA (lignes portant catégorie/VATEX).</param>
    /// <returns><c>true</c> si déclaration B2C taxable (à marquer), <c>false</c> sinon.</returns>
    public static bool IsTaxableB2cDeclaration(PivotDocumentDto enrichedPivot)
    {
        if (enrichedPivot is null)
        {
            return false;
        }

        // (2) Frais d'enchères présents (commission acheteur et/ou vendeur) — discriminant enchères.
        bool hasFees = ((enrichedPivot.SellerFees?.Count ?? 0) > 0) || ((enrichedPivot.BuyerFees?.Count ?? 0) > 0);
        if (!hasFees)
        {
            return false;
        }

        // (1a) TVA distincte au grain document (régime du prix total, art. 297 E a contrario — F03 §2.7).
        if (enrichedPivot.Totals.TotalTax <= 0m)
        {
            return false;
        }

        // (3) Acheteur non professionnel (B2C, F03 §2.7) — prédicat partagé avec la marge (invariant d'aiguillage).
        if (!B2cBuyerClassification.IsNonProfessional(enrichedPivot.Customer))
        {
            return false;
        }

        // (1b) Toutes les lignes taxables à taux positif (S/AA/AAA), issu de la TABLE VALIDÉE (fail-closed).
        return AllLinesTaxable(enrichedPivot.Lines);
    }

    /// <summary>
    /// Vrai si TOUTES les lignes (au moins une) sont mappées à une catégorie taxable à taux positif
    /// (<c>S</c>/<c>AA</c>/<c>AAA</c>). « Toutes » : une ligne exonérée / hors champ mêlée à un lot censé taxable
    /// rend le document ambigu → non marqué (fail-closed, F03 §2.7), jamais agrégé en <c>TLB1</c> à tort.
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
