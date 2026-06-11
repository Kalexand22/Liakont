namespace Liakont.Host.TvaMappingTable;

/// <summary>
/// Vocabulaire d'AFFICHAGE de la « Composante » d'une règle de mapping TVA (décision opérateur E2,
/// lot FIX2) — il remplace le vocabulaire technique « part » / <c>TvaMappingPart</c> côté console.
/// Le libellé de champ est « Composante » et les valeurs affichées sont « Adjudication / Frais /
/// Hors Enchères » dans les CELLULES et LIBELLÉS rendus à l'écran (l'énumération du domaine reste
/// INCHANGÉE — ceci est purement présentationnel, CLAUDE.md n°19 ; le tri / filtre / export de la grille
/// opèrent, eux, sur le code technique brut, comme la colonne Catégorie exporte son code). La notion
/// n'apparaît QUE lorsque le vertical « vente aux enchères » est actif : hors vertical, l'appelant ne
/// rend RIEN (E2 : aucune mention — ni champ, ni valeur, ni note). Source unique de ce vocabulaire pour
/// l'éditeur, la grille, le rapport de cohérence, les messages et le journal des modifications.
/// </summary>
internal static class TvaComposanteVocabulary
{
    /// <summary>Libellé de la notion (remplace « Part » partout dans la console, vertical actif).</summary>
    public const string FieldLabel = "Composante";

    /// <summary>
    /// Libellé d'affichage d'une valeur de composante : <c>Autre</c> → « Hors Enchères » ; les autres
    /// valeurs (<c>Adjudication</c>, <c>Frais</c>) s'affichent telles quelles. Une valeur inconnue
    /// retombe sur elle-même (jamais inventée) ; <c>null</c>/vide donne une chaîne vide.
    /// </summary>
    public static string ValueLabel(string? part) => part switch
    {
        "Adjudication" => "Adjudication",
        "Frais" => "Frais",
        "Autre" => "Hors Enchères",
        null or "" => string.Empty,
        _ => part,
    };
}
