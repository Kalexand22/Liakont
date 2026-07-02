namespace Liakont.Modules.Ged.Domain.Mapping;

/// <summary>
/// Règle déclarative de mapping d'une relation document→entité (F19 §4.5). Désigne dans la source BRUTE
/// l'identifiant externe de l'entité cible ; le <see cref="Kind"/> et le <see cref="TargetType"/> sont du
/// paramétrage tenant (jamais un vocabulaire métier en dur — règle 7).
/// </summary>
/// <param name="Kind">Nature de la relation (paramétrage tenant, ex. « concerne »).</param>
/// <param name="TargetType">Code du type d'entité cible de la relation.</param>
/// <param name="TargetExternalIdSource">Sélecteur JSONPath restreint désignant l'identifiant externe de la cible.</param>
public sealed record RelationMappingRule(string Kind, string TargetType, string TargetExternalIdSource);
