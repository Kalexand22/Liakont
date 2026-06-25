namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Dérivation PURE (sans I/O) du marqueur de DÉCLARATION DE MARGE B2C (flux 10.3, enchères) posé par la
/// PLATEFORME sur <see cref="PivotDocumentDto.IsB2cReportingDeclaration"/>. L'agent ne porte JAMAIS ce
/// marqueur (il extrait des pièces source, pas des déclarations — CLAUDE.md n°6) : il est DÉRIVÉ au read-time,
/// au moment où le pivot est enrichi par le mapping TVA VALIDÉ du tenant (catégorie + VATEX par ligne), comme
/// l'émetteur et la classification TVA (pattern PivotEmitterEnricher / CheckTvaMapping). Il n'est jamais
/// persisté (le staging garde le pivot SOURCE, hashé pour l'anti-doublon F06).
///
/// <para><b>Critère SOURCÉ (F03), AUCUNE règle inventée (CLAUDE.md n°2) :</b></para>
/// <list type="number">
///   <item><b>Régime de la marge</b> : toutes les lignes (adjudication) sont mappées <c>E</c> + un VATEX de
///   marge (<c>VATEX-EU-F</c>/<c>I</c>/<c>J</c> — biens d'occasion / œuvres d'art / objets de collection,
///   F03 §2.2/§2.3). Le régime de la marge ne se DÉDUIT JAMAIS mécaniquement du code source (F03 §3,
///   décision #1) : ce signal vient de la TABLE VALIDÉE régime-par-régime par l'expert-comptable. Une
///   adjudication <c>E</c> SANS VATEX de marge (cas ambigu « marge ou hors champ ? », F03 §3) → NON marquée
///   (fail-closed, jamais devinée).</item>
///   <item><b>Aucune TVA distincte</b> (<see cref="PivotTotalsDto.TotalTax"/> = 0) — art. 297 E (F03 §2.3) :
///   sous le régime de la marge la TVA ne figure jamais distinctement au grain document.</item>
///   <item><b>Frais de marge présents</b> (acheteur OU vendeur) — la marge EST la commission totale du
///   commissaire-priseur (F03 §2.4, BOI-TVA-SECT-90-50 §270).</item>
///   <item><b>B2C</b> : acheteur NON professionnel (commettant non assujetti / acquéreur particulier,
///   F03 §2.4). Un acheteur professionnel (SIREN, n° TVA, indice société) relève du B2B (e-invoicing),
///   jamais de l'e-reporting B2C de la marge.</item>
/// </list>
///
/// <para>La détection « non professionnel » s'appuie UNIQUEMENT sur les champs du contrat pivot
/// (<see cref="PivotPartyDto"/>) — jamais sur <c>Validation.Domain.CompanyHintDetector</c> : le module
/// Pipeline n'accède pas au Domain d'un autre module (frontière P1, CLAUDE.md n°14). Les acheteurs « pseudo-pro »
/// résiduels (forme juridique dans le nom, sans SIREN) sont par ailleurs BLOQUÉS en amont par
/// <c>BuyerLooksProfessionalRule</c> (Validation) : ils n'atteignent jamais ce marquage non tranché.</para>
/// </summary>
public static class B2cMarginMarking
{
    /// <summary>
    /// Codes VATEX du régime de la marge (F03 §2.2) : <c>F</c> = biens d'occasion, <c>I</c> = œuvres d'art,
    /// <c>J</c> = objets de collection et d'antiquité. Seuls ces codes (issus de la table VALIDÉE) signalent
    /// sans ambiguïté le régime de la marge — comparaison ORDINALE stricte (jamais une glose).
    /// </summary>
    private static readonly HashSet<string> MarginVatexCodes = new(System.StringComparer.Ordinal)
    {
        "VATEX-EU-F",
        "VATEX-EU-I",
        "VATEX-EU-J",
    };

    /// <summary>
    /// Vrai si <paramref name="enrichedPivot"/> (DÉJÀ enrichi par le mapping TVA validé : catégorie + VATEX
    /// posés sur les lignes) qualifie une déclaration de marge B2C (flux 10.3). Fail-closed : tout signal
    /// manquant ou ambigu → <c>false</c> (jamais marqué à tort). Le marqueur résultant est consommé par
    /// <see cref="B2cMarginDeclaration.Matches"/> pour aiguiller le document vers le job agrégé (B4).
    /// </summary>
    /// <param name="enrichedPivot">Le pivot enrichi par le mapping TVA (lignes portant catégorie/VATEX).</param>
    /// <returns><c>true</c> si déclaration de marge B2C (à marquer), <c>false</c> sinon.</returns>
    public static bool IsMarginDeclaration(PivotDocumentDto enrichedPivot)
    {
        if (enrichedPivot is null)
        {
            return false;
        }

        // (3) Frais de marge présents (commission acheteur et/ou vendeur, F03 §2.4).
        bool hasFees = ((enrichedPivot.SellerFees?.Count ?? 0) > 0) || ((enrichedPivot.BuyerFees?.Count ?? 0) > 0);
        if (!hasFees)
        {
            return false;
        }

        // (2) Aucune TVA distincte au grain document (art. 297 E, F03 §2.3).
        if (enrichedPivot.Totals.TotalTax != 0m)
        {
            return false;
        }

        // (4) Acheteur non professionnel (B2C, F03 §2.4) — prédicat partagé avec le taxable (invariant d'aiguillage).
        if (!B2cBuyerClassification.IsNonProfessional(enrichedPivot.Customer))
        {
            return false;
        }

        // (1) Régime de la marge issu de la TABLE VALIDÉE (E + VATEX-EU-F/I/J, F03 §2.2/§2.3/§3).
        return AllLinesUnderMarginRegime(enrichedPivot.Lines);
    }

    /// <summary>
    /// Vrai si le document a la FORME d'une déclaration de marge B2C — frais de marge présents ET aucune TVA
    /// distincte (art. 297 E : <see cref="PivotTotalsDto.TotalTax"/> = 0) — mais N'EST PAS classé marge par
    /// <see cref="IsMarginDeclaration"/> (régime exonéré NON reconnu marge : E + VATEX non-marge / hors champ ;
    /// ou acheteur PROFESSIONNEL sous régime exonéré). C'est le cas ambigu de F03 §6 décision #1 (« non
    /// assujetti : marge ou hors champ ? ») ou un mapping incomplet. Transmis tel quel par la voie document, les
    /// honoraires (DONNÉE DE CALCUL, portés HORS lignes) seraient PERDUS → marge sous-déclarée SANS message
    /// opérateur. À BLOQUER au CHECK (fail-closed, CLAUDE.md n°3), jamais routé en silence — symétrique au
    /// pré-filtre du job agrégé B4.
    /// <para>Un document TAXABLE (TVA distincte &gt; 0) n'est PAS visé : il s'émet par sa voie nominale ; la
    /// représentation de sa commission en ligne taxable relève de l'adaptateur source, hors de ce maillon.</para>
    /// <para>Un EXPORT HORS UE détaxé (catégorie <c>G</c>, art. 262 I — <see cref="B2cExportMarking"/>) partage la
    /// forme « frais + TotalTax == 0 » de la marge MAIS est RECONNU (classé export, déféré vers le job unitaire) :
    /// il est EXCLU ici, sinon un export valide serait faussement bloqué « marge non classée ». Exclusion
    /// symétrique à celle de <see cref="B2cMarginDeclaration.Matches"/>.</para>
    /// </summary>
    /// <param name="enrichedPivot">Le pivot enrichi par le mapping TVA (lignes portant catégorie/VATEX).</param>
    /// <returns><c>true</c> si le document a la forme d'une marge mais n'est pas classé marge (à bloquer).</returns>
    public static bool LooksLikeUnclassifiedMargin(PivotDocumentDto enrichedPivot)
    {
        if (enrichedPivot is null)
        {
            return false;
        }

        bool hasFees = ((enrichedPivot.SellerFees?.Count ?? 0) > 0) || ((enrichedPivot.BuyerFees?.Count ?? 0) > 0);
        return hasFees
            && enrichedPivot.Totals.TotalTax == 0m
            && !IsMarginDeclaration(enrichedPivot)
            && !B2cExportMarking.IsExportDeclaration(enrichedPivot);
    }

    /// <summary>
    /// Vrai si TOUTES les lignes sont mappées au régime de la marge (catégorie <c>E</c> + un VATEX de marge).
    /// « Toutes » (et au moins une) : un document marge est exonéré de TVA distincte sur l'intégralité de ses
    /// lignes (l'adjudication) ; une ligne taxable mêlée → ce n'est pas une marge pure → non marqué (fail-closed).
    /// </summary>
    private static bool AllLinesUnderMarginRegime(IReadOnlyList<PivotLineDto> lines)
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
            if (tax.CategoryCode != VatCategory.E || tax.VatexCode is null || !MarginVatexCodes.Contains(tax.VatexCode))
            {
                return false;
            }
        }

        return true;
    }
}
