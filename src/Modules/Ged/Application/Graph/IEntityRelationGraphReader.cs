namespace Liakont.Modules.Ged.Application.Graph;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ged.Domain.Graph;

/// <summary>
/// Lecture BORNÉE du graphe entité↔entité pour le moteur d'inférence (F19 §6.4/§10, GED24), tenant-scopée par la
/// connexion (INV-GED-08). Ne lit que les vues <c>current_*</c> (exclut rétractées/superséedées, RL-24) et
/// borne strictement l'exploration (anti-DoS) — jamais de chargement-tout.
/// </summary>
public interface IEntityRelationGraphReader
{
    /// <summary>
    /// Charge le SUBSTRAT asserté (<c>relation_type</c> ∈ {<c>direct</c>, <c>extracted</c>}) du voisinage
    /// AVANT-atteignable depuis <paramref name="seedEntityId"/> dans une limite de <paramref name="maxDepth"/>
    /// sauts (CTE récursif borné, sans OFFSET) : l'ensemble d'arêtes que le moteur traverse. Le substrat exclut
    /// toute arête touchant une entité CONFIDENTIELLE (confidentialité héritée des <c>entity_types</c> aux deux
    /// extrémités, RL-31/INV-GED-10, fail-closed).
    /// </summary>
    Task<IReadOnlyList<EntityRelationEdge>> LoadAssertedNeighbourhoodAsync(
        Guid seedEntityId,
        int maxDepth,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retourne les relations DÉJÀ courantes SORTANT de la graine, en <c>(cible, genre)</c> — quel que soit leur
    /// <c>relation_type</c> — pour exclure toute relation déjà présente (idempotence de l'inférence).
    /// </summary>
    Task<IReadOnlyList<(Guid ToEntityId, string RelationKind)>> LoadCurrentOutRelationsAsync(
        Guid seedEntityId,
        CancellationToken cancellationToken = default);
}
