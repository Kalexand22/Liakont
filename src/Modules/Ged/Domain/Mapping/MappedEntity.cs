namespace Liakont.Modules.Ged.Domain.Mapping;

/// <summary>
/// Entité MAPPÉE (F19 §4.4/§4.5) : type cible + identifiant externe (clé de réconciliation) + libellé
/// d'affichage éventuel. La résolution d'identité (upsert idempotent) et l'écriture des liens sont du ressort
/// du consommateur d'ingestion (GED05b) ; le mapper ne fait que DÉCLARER l'entité à créer/rattacher.
/// </summary>
/// <param name="EntityType">Code du type d'entité cible.</param>
/// <param name="ExternalId">Identifiant externe brut (clé de réconciliation).</param>
/// <param name="Display">Libellé d'affichage, ou <see langword="null"/>.</param>
public sealed record MappedEntity(string EntityType, string ExternalId, string? Display);
