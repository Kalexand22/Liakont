namespace Liakont.Host.Components;

using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Libellé d'affichage FRANÇAIS d'une catégorie de TVA (code UNCL5305, EN 16931 BT-151) pour la console
/// (onglet « Contenu » du détail document, F10 §2.3). Les libellés sont <b>TRANSCRITS de F03-Mapping-TVA.md
/// §2.1</b> — exactement le vocabulaire de l'éditeur de table TVA (<c>GetTvaMappingEditOptionsHandler</c>) :
/// AUCUN libellé fiscal n'est inventé (CLAUDE.md n°2). Fonction TOTALE et PURE d'affichage : un code sans
/// libellé connu retombe sur son code brut, une catégorie absente (<c>null</c> — mapping non encore tranché)
/// retombe sur « — ». Aucune règle métier (la classification reste dans le module TVA, lot F03).
/// </summary>
public static class VatCategoryDisplay
{
    // Libellés UNCL5305 transcrits de F03 §2.1 (et des commentaires de l'enum VatCategory) — mêmes valeurs
    // que CategoryLabels dans GetTvaMappingEditOptionsHandler, transcription de la MÊME source pour ne pas
    // diverger. Les CLÉS sont les noms de l'enum (la source des codes), jamais une liste inventée.
    private static readonly Dictionary<VatCategory, string> Labels = new()
    {
        [VatCategory.S] = "Taux normal",
        [VatCategory.AA] = "Taux réduit",
        [VatCategory.AAA] = "Taux particulier (super-réduit)",
        [VatCategory.Z] = "Taux zéro (assujetti)",
        [VatCategory.E] = "Exonéré (motif VATEX requis)",
        [VatCategory.AE] = "Autoliquidation",
        [VatCategory.G] = "Export hors UE détaxé",
        [VatCategory.K] = "Livraison/prestation intracommunautaire",
        [VatCategory.O] = "Hors champ d'application de la TVA",
    };

    /// <summary>
    /// Libellé « <c>CODE — Libellé</c> » pour une catégorie (ex. « S — Taux normal »). <c>null</c> (mapping non
    /// encore tranché) → « — » ; un code sans libellé transcrit → le code seul (jamais de libellé inventé).
    /// </summary>
    /// <param name="category">La catégorie résolue par le mapping plateforme, ou <c>null</c> si non mappée.</param>
    public static string For(VatCategory? category)
    {
        if (category is not { } value)
        {
            return "—";
        }

        return Labels.TryGetValue(value, out var label)
            ? $"{value} — {label}"
            : value.ToString();
    }
}
