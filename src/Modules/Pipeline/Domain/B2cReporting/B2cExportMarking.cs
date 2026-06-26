namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Dérivation PURE (sans I/O) du marqueur de DÉCLARATION B2C d'EXONÉRÉ INTERNATIONAL (flux 10.3, enchères) posé
/// par la PLATEFORME sur <see cref="PivotDocumentDto.IsB2cReportingDeclaration"/>. TROISIÈME cas symétrique de
/// <see cref="B2cMarginMarking"/> (marge, TMA1) et <see cref="B2cTaxableMarking"/> (prix total, TLB1) : même flux
/// 10.3, même canal (acheteur particulier → e-reporting B2C), mais l'opération est détaxée et internationale —
/// transmise en e-reporting **UNITAIRE** (une transaction par opération, F03 §2.8), portée par
/// <c>B2cExportReportingTenantJob</c>, à la différence de la marge/prix total domestiques (agrégés jour×devise).
/// Couvre TROIS régimes détaxés (catégorie UNCL5305 issue de la TABLE VALIDÉE, par zone composite de l'agent) :
/// <list type="bullet">
///   <item><b>Export hors UE</b> (art. 262 I) → catégorie <c>G</c> → TT-81 <c>TLB1</c> (livraison de biens).</item>
///   <item><b>Intracommunautaire</b> (art. 262 ter / 258 A) → catégorie <c>K</c> → TT-81 <c>TNT1</c> (non soumis
///   en France ; l'éventuelle taxe à destination OSS est un autre flux).</item>
///   <item><b>Franchise</b> (art. 275, achat en franchise en vue d'export) → catégorie <c>G</c> → <c>TLB1</c>
///   (livraison détaxée export-bound, même structure que 262 I).</item>
/// </list>
/// La résolution catégorie → TT-81 vit dans le job (l'enum TT-81 est en Transmission.Contracts, hors Domain).
/// Comme les deux autres marqueurs, il est DÉRIVÉ au read-time sur le pivot ENRICHI par le mapping TVA validé
/// (catégorie par ligne), jamais porté par l'agent ni persisté (pattern émetteur/TVA).
///
/// <para><b>Critère (F03 §2.8), AUCUNE règle inventée (CLAUDE.md n°2 — classification fiscale validée PO) :</b></para>
/// <list type="number">
///   <item><b>Détaxé international</b> : TOUTES les lignes (l'adjudication) sont mappées à UNE MÊME catégorie
///   détaxée <c>G</c> (export/franchise) OU <c>K</c> (intracom), issue de la TABLE VALIDÉE — F03 §2.1/§2.8.
///   <c>G</c>/<c>K</c> sont des signaux FERMÉS et NON ambigus (à la différence de <c>E</c>, partagé marge/hors
///   champ). Lignes hétérogènes (catégories mêlées) ou TVA résiduelle (<c>TaxAmount &gt; 0</c>) → non marqué
///   (fail-closed, TT-81 indéterminée).</item>
///   <item><b>Aucune TVA distincte</b> (<see cref="PivotTotalsDto.TotalTax"/> == 0) — détaxé : la TVA ne figure
///   jamais au grain document. Même signal de tête que la marge (art. 297 E) ; la distinction vient de la
///   catégorie de ligne (<c>G</c>/<c>K</c> détaxé vs <c>E</c> marge).</item>
///   <item><b>Frais d'enchères présents</b> (commission acheteur et/ou vendeur) — discriminant ENCHÈRES : une
///   facture B2C exonérée ORDINAIRE (sans frais) suit sa voie document nominale, jamais le job d'export.</item>
///   <item><b>B2C</b> : acheteur NON professionnel (<see cref="B2cBuyerClassification.IsNonProfessional"/>,
///   prédicat PARTAGÉ — invariant d'aiguillage). Un acquéreur professionnel relève du B2B (e-invoicing).</item>
/// </list>
/// </summary>
public static class B2cExportMarking
{
    /// <summary>
    /// Catégories UNCL5305 détaxées internationales (F03 §2.8) qualifiant une déclaration d'exonéré international :
    /// <c>G</c> (export hors UE 262 I / franchise 275) et <c>K</c> (intracommunautaire 262 ter / 258 A). Toutes
    /// reportées au TAUX 0 ; la TT-81 (<c>TLB1</c> / <c>TNT1</c>) est résolue par le job depuis la catégorie.
    /// </summary>
    private static readonly HashSet<VatCategory> ExoneratedInternationalCategories = new()
    {
        VatCategory.G,
        VatCategory.K,
    };

    /// <summary>
    /// Vrai si <paramref name="enrichedPivot"/> (DÉJÀ enrichi par le mapping TVA validé : catégorie posée sur les
    /// lignes) qualifie une déclaration B2C d'exonéré international détaxé (flux 10.3, catégorie <c>G</c> ou
    /// <c>K</c> homogène — export 262 I / intracom 262 ter / franchise 275, F03 §2.8). Fail-closed : tout signal
    /// manquant ou ambigu → <c>false</c> (jamais marqué à tort).
    /// </summary>
    /// <param name="enrichedPivot">Le pivot enrichi par le mapping TVA (lignes portant catégorie).</param>
    /// <returns><c>true</c> si déclaration B2C d'exonéré international (à marquer), <c>false</c> sinon.</returns>
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

        // (2) Aucune TVA distincte au grain document (détaxé : export/intracom/franchise).
        if (enrichedPivot.Totals.TotalTax != 0m)
        {
            return false;
        }

        // (4) Acheteur non professionnel (B2C) — prédicat partagé avec marge/taxable (invariant d'aiguillage).
        if (!B2cBuyerClassification.IsNonProfessional(enrichedPivot.Customer))
        {
            return false;
        }

        // (1) Toutes les lignes mappées à UNE MÊME catégorie détaxée (G/K) issue de la TABLE VALIDÉE (fail-closed).
        return AllLinesExonerated(enrichedPivot.Lines);
    }

    /// <summary>
    /// Catégorie détaxée internationale HOMOGÈNE du document (<c>G</c> ou <c>K</c>), ou <c>null</c> si le document
    /// n'est pas un exonéré international reconnu. Utilisée par le job pour résoudre la TT-81 (<c>G</c>→<c>TLB1</c>,
    /// <c>K</c>→<c>TNT1</c>) sans recopier la règle de reconnaissance.
    /// </summary>
    /// <param name="enrichedPivot">Le pivot enrichi par le mapping TVA.</param>
    /// <returns>La catégorie détaxée homogène, ou <c>null</c>.</returns>
    public static VatCategory? ExoneratedCategory(PivotDocumentDto enrichedPivot) =>
        IsExportDeclaration(enrichedPivot) ? enrichedPivot.Lines[0].Taxes[0].CategoryCode : null;

    /// <summary>
    /// Vrai si TOUTES les lignes (au moins une) sont mappées à UNE MÊME catégorie détaxée internationale
    /// (<c>G</c> export/franchise ou <c>K</c> intracom) ET ne portent AUCUNE TVA (<c>TaxAmount == 0</c>, cohérent
    /// avec « tax not charged »). « Même » : la TT-81 (<c>TLB1</c>/<c>TNT1</c>) doit être déterminable sans
    /// ambiguïté — des catégories mêlées ou une TVA résiduelle → non marqué (fail-closed, F03 §2.8).
    /// </summary>
    private static bool AllLinesExonerated(IReadOnlyList<PivotLineDto> lines)
    {
        if (lines.Count == 0)
        {
            return false;
        }

        VatCategory? firstCategory = null;
        foreach (var line in lines)
        {
            if (line.Taxes.Count != 1)
            {
                return false;
            }

            var tax = line.Taxes[0];
            if (tax.CategoryCode is not { } category
                || !ExoneratedInternationalCategories.Contains(category)
                || tax.TaxAmount != 0m)
            {
                return false;
            }

            // Homogénéité : toutes les lignes partagent la MÊME catégorie détaxée (TT-81 unique par document).
            firstCategory ??= category;
            if (category != firstCategory)
            {
                return false;
            }
        }

        return true;
    }
}
