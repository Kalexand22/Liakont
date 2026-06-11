namespace Liakont.Host.TvaMappingTable;

/// <summary>
/// Une ligne lisible du diff d'une entrée du journal de la table TVA : un champ, sa valeur AVANT et sa
/// valeur APRÈS. Pour un ajout, seul <see cref="After"/> est renseigné (valeur créée) ; pour une
/// suppression, seul <see cref="Before"/> (valeur supprimée) ; pour une modification, les deux.
/// </summary>
internal sealed record MappingChangeDiffLine(string Field, string? Before, string? After);
