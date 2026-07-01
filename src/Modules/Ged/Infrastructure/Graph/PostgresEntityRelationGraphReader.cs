namespace Liakont.Modules.Ged.Infrastructure.Graph;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Ged.Application.Graph;
using Liakont.Modules.Ged.Domain.Graph;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lecture Dapper BORNÉE du graphe entité↔entité (F19 §6.4/§10, GED24), sur la base DU TENANT (isolation = la
/// connexion, INV-GED-08). Ne lit que <c>current_entity_relations</c> (exclut rétractées/superséedées, RL-24) et
/// BORNE l'exploration par un CTE récursif à profondeur limitée (anti-DoS ; aucun OFFSET, aucun chargement-tout).
/// Le substrat exclut aussi toute arête touchant une entité CONFIDENTIELLE (RL-31/INV-GED-10, fail-closed) :
/// la confidentialité s'hérite des <c>entity_types</c> aux deux extrémités.
/// Colonnes <c>snake_case</c> aliasées (Dapper n'active pas <c>MatchNamesWithUnderscores</c> ici).
/// </summary>
internal sealed class PostgresEntityRelationGraphReader : IEntityRelationGraphReader
{
    // Voisinage AVANT-atteignable depuis la graine, sur le SUBSTRAT ASSERTÉ (direct/extracted) et NON CONFIDENTIEL.
    // Deux bornes/gardes (miroir du CTE de référence F19 §6.4) :
    //  1. anti-cycle par TABLEAU DE CHEMIN (`NOT ... = ANY(path)`) : borne le nombre de marches (pas seulement la
    //     profondeur), donc le nombre de lignes matérialisées (anti-DoS réel, pas seulement la terminaison) ;
    //  2. CONFIDENTIALITÉ (RL-31 / INV-GED-10) : la confidentialité d'une relation s'hérite des entity_types à ses
    //     DEUX extrémités. L'inférence NE TRAVERSE JAMAIS une entité confidentielle (fail-closed) — une dérivée A→C
    //     via un intermédiaire confidentiel serait IRRÉCUPÉRABLE au read (elle ne mémorise pas son chemin), déplaçant
    //     le canal de fuite de l'axe vers le graphe (interdit §6.5). On exclut donc toute entité confidentielle du substrat.
    private const string NeighbourhoodSql = """
        WITH RECURSIVE
        safe_edges AS (
            SELECT r.from_entity_id, r.to_entity_id, r.relation_kind
            FROM ged_index.current_entity_relations r
            JOIN ged_index.entity_instances fi ON fi.id = r.from_entity_id
            JOIN ged_catalog.entity_types  ft ON ft.id = fi.entity_type_id
            JOIN ged_index.entity_instances ti ON ti.id = r.to_entity_id
            JOIN ged_catalog.entity_types  tt ON tt.id = ti.entity_type_id
            WHERE r.relation_type IN ('direct', 'extracted')
              AND ft.is_confidential = false
              AND tt.is_confidential = false
        ),
        reachable(entity_id, depth, path) AS (
            SELECT @Seed::uuid, 0, ARRAY[@Seed::uuid]
            UNION ALL
            SELECT e.to_entity_id, reach.depth + 1, reach.path || e.to_entity_id
            FROM reachable reach
            JOIN safe_edges e ON e.from_entity_id = reach.entity_id
            WHERE reach.depth < @MaxDepth
              AND NOT e.to_entity_id = ANY(reach.path)
        )
        SELECT DISTINCT e.from_entity_id AS FromEntityId,
                        e.to_entity_id   AS ToEntityId,
                        e.relation_kind  AS RelationKind
        FROM safe_edges e
        WHERE e.from_entity_id IN (SELECT entity_id FROM reachable)
        """;

    // Relations DÉJÀ courantes sortant de la graine (tout relation_type) — clés d'exclusion (idempotence).
    private const string CurrentOutSql = """
        SELECT DISTINCT to_entity_id  AS ToEntityId,
                        relation_kind AS RelationKind
        FROM ged_index.current_entity_relations
        WHERE from_entity_id = @Seed
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresEntityRelationGraphReader(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<EntityRelationEdge>> LoadAssertedNeighbourhoodAsync(
        Guid seedEntityId,
        int maxDepth,
        CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<EdgeRow>(new CommandDefinition(
            NeighbourhoodSql,
            new { Seed = seedEntityId, MaxDepth = maxDepth },
            cancellationToken: cancellationToken));

        return rows
            .Select(r => new EntityRelationEdge(r.FromEntityId, r.ToEntityId, r.RelationKind))
            .ToList();
    }

    public async Task<IReadOnlyList<(Guid ToEntityId, string RelationKind)>> LoadCurrentOutRelationsAsync(
        Guid seedEntityId,
        CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<OutRow>(new CommandDefinition(
            CurrentOutSql,
            new { Seed = seedEntityId },
            cancellationToken: cancellationToken));

        return rows
            .Select(r => (r.ToEntityId, r.RelationKind))
            .ToList();
    }

    private sealed class EdgeRow
    {
        public Guid FromEntityId { get; set; }

        public Guid ToEntityId { get; set; }

        public string RelationKind { get; set; } = string.Empty;
    }

    private sealed class OutRow
    {
        public Guid ToEntityId { get; set; }

        public string RelationKind { get; set; } = string.Empty;
    }
}
