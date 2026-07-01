namespace Liakont.Modules.Ged.Domain.Mapping;

/// <summary>
/// Relation document→entité MAPPÉE (F19 §4.5) : nature + type d'entité cible + identifiant externe de la cible.
/// Générique (aucun vocabulaire métier en dur — règle 7) ; l'écriture du lien graphe est du ressort de GED05b.
/// </summary>
/// <param name="Kind">Nature de la relation.</param>
/// <param name="TargetType">Code du type d'entité cible.</param>
/// <param name="TargetExternalId">Identifiant externe brut de la cible.</param>
public sealed record MappedRelation(string Kind, string TargetType, string TargetExternalId);
