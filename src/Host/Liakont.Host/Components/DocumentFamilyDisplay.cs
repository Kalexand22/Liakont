namespace Liakont.Host.Components;

using System;

/// <summary>
/// Libellé d'affichage FRANÇAIS de la FAMILLE de pièce (BUG-20), dérivée du préfixe de la référence source
/// (convention <c>&lt;adaptateur&gt;:&lt;famille&gt;:&lt;id&gt;</c>). Sur le vertical enchères (adaptateur EncheresV6, seul
/// en V1) la source préfixe chaque pièce par sa famille : <c>encheresv6:ba:</c> (bordereau acheteur),
/// <c>:bv:</c> (bordereau vendeur), <c>:fc:</c> (facture client), <c>:nh:</c> (note d'honoraires). Fonction
/// TOTALE et PURE d'affichage (aucune règle métier, CLAUDE.md n°2/19) : un préfixe non reconnu (autre
/// adaptateur, référence d'un autre format) retombe sur <c>null</c> — la vue n'affiche alors aucune famille,
/// jamais une famille devinée. La distinction de famille complète la colonne « Type » (facture/avoir), qui
/// ne sépare PAS un bordereau acheteur d'un bordereau vendeur (souvent tous deux « Facture »).
/// </summary>
public static class DocumentFamilyDisplay
{
    /// <summary>
    /// Libellé français de la famille de pièce dérivé du segment FAMILLE de la référence source, ou
    /// <c>null</c> si la référence est vide, mal formée, ou porte une famille non reconnue.
    /// </summary>
    /// <param name="sourceReference">Référence source du document (ex. <c>encheresv6:ba:9000004</c>).</param>
    public static string? For(string? sourceReference)
    {
        if (string.IsNullOrWhiteSpace(sourceReference))
        {
            return null;
        }

        // Convention : <adaptateur>:<famille>:<id>. On lit le 2e segment (la famille), insensible à la casse.
        // Une référence sans ce format (moins de 3 segments) ou une famille inconnue → null (jamais devinée).
        var segments = sourceReference.Split(':');
        if (segments.Length < 3)
        {
            return null;
        }

        return segments[1].Trim().ToLowerInvariant() switch
        {
            "ba" => "Bordereau acheteur",
            "bv" => "Bordereau vendeur",
            "fc" => "Facture client",
            "nh" => "Note d'honoraires",
            _ => null,
        };
    }
}
