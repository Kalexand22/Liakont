namespace Liakont.Host.TvaMappingTable;

/// <summary>
/// Une ligne lisible et DÉJÀ RENDUE du diff d'une entrée du journal de la table TVA : un champ et sa
/// valeur d'affichage. Le formateur — seul à savoir si l'entrée est bilatérale (modification /
/// validation : « avant → après », « (vide) » pour un champ ajouté ou retiré) ou mono-versant (ajout /
/// suppression / création : valeur unique) — produit directement <see cref="Value"/> ; la vue n'a aucune
/// décision de rendu à reprendre (source unique de vérité).
/// </summary>
internal sealed record MappingChangeDiffLine(string Field, string Value);
