namespace Liakont.Modules.Ged.Contracts.Commands;

using System;
using MediatR;

/// <summary>
/// Déclenche l'inférence/héritage BORNÉ des relations d'une entité GRAINE (F19 §10, GED24). Le handler charge les
/// règles tenant actives + le voisinage asserté borné de la graine, calcule les relations dérivées
/// (<c>inferred</c>/<c>inherited</c>) et les APPENDE dans <c>ged_index.entity_relations</c> (idempotent). Rend le
/// nombre de relations dérivées ajoutées. Tenant-scopé par la connexion (INV-GED-08).
/// </summary>
public sealed record InferEntityRelationsCommand : IRequest<int>
{
    /// <summary>Entité graine dont on dérive les relations (from_entity_id des relations produites).</summary>
    public required Guid SeedEntityId { get; init; }

    /// <summary>
    /// Canal de provenance des relations dérivées écrites (<c>agent|manual|ai|import|ocr</c>, miroir
    /// <c>ck_er_source</c>). La provenance de DÉRIVATION (transitive/héritage) est portée par
    /// <c>relation_type</c> ; ce champ décrit le canal qui a déclenché l'inférence.
    /// </summary>
    public required string Source { get; init; }
}
