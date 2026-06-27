namespace Liakont.Host.Components;

using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Mention d'affichage FRANÇAISE explicite du RÉGIME DE LA MARGE (art. 297 A/E) pour une ligne de document dont
/// la TVA est à 0 — afin qu'un opérateur (enchères, brocante, biens d'occasion) ne la confonde pas avec une
/// exonération « classique ». Le sec « E — Exonéré (motif VATEX requis) » de <see cref="VatCategoryDisplay"/>
/// prête à confusion sous la marge : la TVA y est nulle PARCE QUE c'est le régime de la marge (TVA dans la marge,
/// non récupérable par l'acheteur — art. 297 E), pas une exonération ordinaire.
///
/// <para>Le régime de la marge se reconnaît à sa SIGNATURE déjà mappée par la plateforme : catégorie <c>E</c>
/// (exonéré) + un code VATEX de marge (<c>VATEX-EU-F</c>/<c>I</c>/<c>J</c>, F03 §2.2/§2.3) — le MÊME critère que
/// <c>B2cMarginMarking</c>. Fonction PURE et TOTALE : une ligne qui n'est PAS au régime de la marge → <c>null</c>
/// (elle s'affiche normalement). AUCUNE règle fiscale inventée (CLAUDE.md n°2) : la nature (biens d'occasion /
/// œuvres d'art / objets de collection) est TRANSCRITE du tableau VATEX de F03 §2.2 (mêmes libellés que
/// <c>VatexCatalog</c>), jamais déduite du code source. Aucune logique métier : on LIT la catégorie/le VATEX déjà
/// tranchés par le mapping plateforme.</para>
/// </summary>
public static class MarginRegimeDisplay
{
    // Nature du bien par code VATEX de marge — transcription de F03 §2.2 (cf. VatexCatalog) : F = biens
    // d'occasion, I = œuvres d'art, J = objets de collection et d'antiquité. Liste FERMÉE (les mêmes 3 codes
    // que B2cMarginMarking), comparaison ORDINALE stricte (jamais une glose).
    private static readonly Dictionary<string, string> MarginNatureByVatex = new(System.StringComparer.Ordinal)
    {
        ["VATEX-EU-F"] = "biens d'occasion",
        ["VATEX-EU-I"] = "œuvres d'art",
        ["VATEX-EU-J"] = "objets de collection et d'antiquité",
    };

    /// <summary>
    /// Mention « Régime de la marge – &lt;nature&gt; » si la ligne est au régime de la marge (catégorie <c>E</c>
    /// + un VATEX de marge), sinon <c>null</c> (la ligne s'affiche avec sa catégorie nominale). PURE.
    /// </summary>
    /// <param name="category">La catégorie résolue par le mapping plateforme, ou <c>null</c> si non mappée.</param>
    /// <param name="vatexCode">Le code VATEX résolu par le mapping plateforme, ou <c>null</c> si absent.</param>
    public static string? For(VatCategory? category, string? vatexCode)
    {
        if (category != VatCategory.E || vatexCode is null)
        {
            return null;
        }

        return MarginNatureByVatex.TryGetValue(vatexCode, out var nature)
            ? $"Régime de la marge – {nature}"
            : null;
    }
}
