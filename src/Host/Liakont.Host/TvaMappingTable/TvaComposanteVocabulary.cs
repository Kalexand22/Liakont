namespace Liakont.Host.TvaMappingTable;

/// <summary>
/// Vocabulaire d'AFFICHAGE de la « Composante » d'une règle de mapping TVA (décision opérateur E2,
/// lot FIX2) — il remplace le vocabulaire technique « part » / <c>TvaMappingPart</c> côté console.
/// Le libellé de champ est « Composante » et les valeurs affichées sont « Frais / Adjudication et
/// factures » dans les CELLULES et LIBELLÉS rendus à l'écran (l'énumération du domaine reste
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
    /// Libellé d'affichage d'une valeur de composante (BUG-12) : <c>Autre</c> → « Adjudication et factures »
    /// (c'est la part lue par le CHECK pour TOUTES les lignes — adjudication, factures clients, notes — d'où
    /// un libellé qui couvre aussi les cas hors enchères) ; <c>Frais</c> → « Frais » (taux des honoraires).
    /// <c>Adjudication</c> (part MORTE, jamais consultée) garde son libellé pour les règles héritées éventuelles.
    /// Une valeur inconnue retombe sur elle-même (jamais inventée) ; <c>null</c>/vide donne une chaîne vide.
    /// </summary>
    public static string ValueLabel(string? part) => part switch
    {
        "Adjudication" => "Adjudication",
        "Frais" => "Frais",
        "Autre" => "Adjudication et factures",
        null or "" => string.Empty,
        _ => part,
    };
}
