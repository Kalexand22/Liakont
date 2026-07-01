namespace Liakont.Modules.Ged.Domain.Graph;

using System;

/// <summary>
/// Relation DÉRIVÉE produite par le <see cref="RelationInferenceEngine"/> (F19 §10, GED24), à APPENDER dans
/// <c>ged_index.entity_relations</c> avec sa provenance <see cref="RelationType"/> (<c>inferred</c> pour la
/// fermeture transitive, <c>inherited</c> pour l'héritage hiérarchique). Déterministe → aucun
/// <c>confidence_score</c> (null) à l'écriture.
/// </summary>
/// <param name="FromEntityId">Entité source (toujours la GRAINE de l'inférence — per-seed borné).</param>
/// <param name="ToEntityId">Entité cible dérivée.</param>
/// <param name="RelationKind">Genre métier hérité/inféré (<c>relation_kind</c>).</param>
/// <param name="RelationType"><c>inferred</c> ou <c>inherited</c> (provenance).</param>
public readonly record struct DerivedRelation(
    Guid FromEntityId,
    Guid ToEntityId,
    string RelationKind,
    string RelationType);
