namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Dérivation PURE (sans I/O) du marqueur de DÉCLARATION B2C d'EXPORT HORS UE (flux 10.3, enchères — livraison
/// exonérée art. 262 I CGI) posé par la PLATEFORME sur <see cref="PivotDocumentDto.IsB2cReportingDeclaration"/>.
/// TROISIÈME cas symétrique de <see cref="B2cMarginMarking"/> (marge, TMA1) et <see cref="B2cTaxableMarking"/>
/// (prix total, TLB1) : même flux 10.3, même canal (acheteur particulier → e-reporting B2C), mais l'opération
/// est un EXPORT hors Union européenne — détaxé (catégorie UNCL5305 <c>G</c>, art. 262 I, F03 §2.8). La
/// transmission e-reporting de l'export est UNITAIRE (une transaction par opération internationale, F03 §2.8),
/// portée par <c>B2cExportReportingTenantJob</c> — à la différence de la marge/prix total domestiques (agrégés
/// jour×devise). Comme les deux autres marqueurs, il est DÉRIVÉ au read-time sur le pivot ENRICHI par le
/// mapping TVA validé (catégorie par ligne), jamais porté par l'agent ni persisté (pattern émetteur/TVA).
///
/// <para><b>Critère SOURCÉ (F03 §2.8), AUCUNE règle inventée (CLAUDE.md n°2) :</b></para>
/// <list type="number">
///   <item><b>Export hors UE détaxé</b> : TOUTES les lignes (l'adjudication) sont mappées à la catégorie
///   <c>G</c> (export hors UE, art. 262 I) issue de la TABLE VALIDÉE — F03 §2.1/§2.8. <c>G</c> est un signal
///   FERMÉ et NON ambigu (à la différence de <c>E</c>, partagé marge/hors champ) : seule la zone HORS UE y est
///   mappée (la table laisse l'intracom <c>CEE</c> et la franchise <c>FRANCE</c> NON couvertes → fail-closed,
///   non tranchés EC). Une ligne portant une TVA résiduelle (<c>TaxAmount &gt; 0</c>) contredirait l'export →
///   non marquée (fail-closed).</item>
///   <item><b>Aucune TVA distincte</b> (<see cref="PivotTotalsDto.TotalTax"/> == 0) — un export est exonéré :
///   la TVA ne figure jamais au grain document (art. 262 I). Mêmes signal de tête que la marge (art. 297 E) ; la
///   distinction vient de la catégorie de ligne (<c>G</c> export vs <c>E</c> marge).</item>
///   <item><b>Frais d'enchères présents</b> (commission acheteur et/ou vendeur) — discriminant ENCHÈRES : une
///   facture B2C exonérée ORDINAIRE (sans frais) suit sa voie document nominale, jamais le job d'export.</item>
///   <item><b>B2C</b> : acheteur NON professionnel (<see cref="B2cBuyerClassification.IsNonProfessional"/>,
///   prédicat PARTAGÉ — invariant d'aiguillage). Un acquéreur professionnel relève du B2B (e-invoicing).</item>
/// </list>
/// </summary>
public static class B2cExportMarking
{
    /// <summary>
    /// Vrai si <paramref name="enrichedPivot"/> (DÉJÀ enrichi par le mapping TVA validé : catégorie posée sur
    /// les lignes) qualifie une déclaration B2C d'export hors UE détaxé (flux 10.3, catégorie <c>G</c>, art. 262 I).
    /// Fail-closed : tout signal manquant ou ambigu → <c>false</c> (jamais marqué à tort).
    /// </summary>
    /// <param name="enrichedPivot">Le pivot enrichi par le mapping TVA (lignes portant catégorie).</param>
    /// <returns><c>true</c> si déclaration B2C d'export hors UE (à marquer), <c>false</c> sinon.</returns>
    public static bool IsExportDeclaration(PivotDocumentDto enrichedPivot)
    {
        if (enrichedPivot is null)
        {
            return false;
        }

        // (3) Frais d'enchères présents (commission acheteur et/ou vendeur) — discriminant enchères.
        bool hasFees = ((enrichedPivot.SellerFees?.Count ?? 0) > 0) || ((enrichedPivot.BuyerFees?.Count ?? 0) > 0);
        if (!hasFees)
        {
            return false;
        }

        // (2) Aucune TVA distincte au grain document (export exonéré, art. 262 I).
        if (enrichedPivot.Totals.TotalTax != 0m)
        {
            return false;
        }

        // (4) Acheteur non professionnel (B2C) — prédicat partagé avec marge/taxable (invariant d'aiguillage).
        if (!B2cBuyerClassification.IsNonProfessional(enrichedPivot.Customer))
        {
            return false;
        }

        // (1) Toutes les lignes mappées catégorie G (export hors UE détaxé) issue de la TABLE VALIDÉE (fail-closed).
        return AllLinesExport(enrichedPivot.Lines);
    }

    /// <summary>
    /// Vrai si TOUTES les lignes (au moins une) sont mappées à la catégorie <c>G</c> (export hors UE détaxé) ET
    /// ne portent AUCUNE TVA (<c>TaxAmount == 0</c>, cohérent avec « tax not charged » du code G). « Toutes » :
    /// une ligne non-<c>G</c> ou portant une TVA résiduelle mêlée → ce n'est pas un export pur → non marqué
    /// (fail-closed, F03 §2.8), jamais détaxé à tort.
    /// </summary>
    private static bool AllLinesExport(IReadOnlyList<PivotLineDto> lines)
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

            var tax = line.Taxes[0];
            if (tax.CategoryCode != VatCategory.G || tax.TaxAmount != 0m)
            {
                return false;
            }
        }

        return true;
    }
}
