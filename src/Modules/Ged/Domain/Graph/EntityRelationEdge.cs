namespace Liakont.Modules.Ged.Domain.Graph;

using System;

/// <summary>
/// Arête COURANTE du graphe entité↔entité projetée pour le moteur d'inférence (F19 §3.4.4/§10, GED24) : une
/// relation ASSERTÉE (substrat <c>direct</c>/<c>extracted</c>) lue depuis <c>current_entity_relations</c>. Le
/// moteur ne traverse QUE le substrat asserté (jamais les relations déjà dérivées) → la fermeture converge.
/// </summary>
/// <param name="FromEntityId">Entité source (<c>from_entity_id</c>).</param>
/// <param name="ToEntityId">Entité cible (<c>to_entity_id</c>).</param>
/// <param name="RelationKind">Genre métier déclaré (<c>relation_kind</c>).</param>
public readonly record struct EntityRelationEdge(Guid FromEntityId, Guid ToEntityId, string RelationKind);
