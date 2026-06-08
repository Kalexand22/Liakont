namespace Liakont.Modules.TvaMapping.Contracts.DTOs;

/// <summary>
/// Option d'une liste FERMÉE d'édition de la table de mapping TVA (<see cref="TvaMappingEditOptionsDto"/>) :
/// le <see cref="Code"/> est la valeur transmise (exactement celle attendue par les commandes TVA05), le
/// <see cref="Label"/> son libellé d'affichage console.
/// </summary>
/// <param name="Code">Valeur transmise (ex. <c>E</c>, <c>Fixed</c>, <c>VATEX-EU-J</c>).</param>
/// <param name="Label">Libellé d'affichage français (sourcé, jamais une règle fiscale dérivée).</param>
public sealed record TvaMappingOptionDto(string Code, string Label);
