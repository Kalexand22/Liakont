namespace Liakont.Modules.Ged.Domain.Mapping;

/// <summary>
/// Règle déclarative de mapping d'une entité (F19 §4.5). Désigne dans la source BRUTE l'identifiant externe
/// (clé de réconciliation, §4.4) d'une entité du type cible <see cref="EntityType"/>, et éventuellement son
/// libellé d'affichage. La règle ne crée jamais une entité sans identifiant : si <see cref="ExternalIdSource"/>
/// ne résout rien, aucun lien d'entité n'est produit (best-effort — l'entité n'est pas un axe obligatoire).
/// </summary>
/// <param name="EntityType">Code du type d'entité cible (paramétrage tenant, jamais en dur — règle 7).</param>
/// <param name="ExternalIdSource">Sélecteur JSONPath restreint désignant l'identifiant externe de l'entité.</param>
/// <param name="DisplaySource">Sélecteur JSONPath restreint désignant le libellé d'affichage, ou <see langword="null"/>.</param>
public sealed record EntityMappingRule(string EntityType, string ExternalIdSource, string? DisplaySource);
