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
/// Colonnes <c>snake_case</c> aliasées (Dapper n'active pas <c>MatchNamesWithUnderscores</c> ici).
/// </summary>
internal sealed class PostgresEntityRelationGraphReader : IEntityRelationGraphReader
{
    // Voisinage AVANT-atteignable depuis la graine sur le SUBSTRAT asserté (direct/extracted), borné en
    // profondeur. Le prédicat `depth < @MaxDepth` garantit la terminaison même sur un graphe cyclique.
    private const string NeighbourhoodSql = """
        WITH RECURSIVE reachable(entity_id, depth) AS (
            SELECT @Seed::uuid, 0
            UNION ALL
            SELECT r.to_entity_id, reach.depth + 1
            FROM reachable reach
            JOIN ged_index.current_entity_relations r
              ON r.from_entity_id = reach.entity_id
             AND r.relation_type IN ('direct', 'extracted')
            WHERE reach.depth < @MaxDepth
        )
        SELECT DISTINCT r.from_entity_id AS FromEntityId,
                        r.to_entity_id   AS ToEntityId,
                        r.relation_kind  AS RelationKind
        FROM ged_index.current_entity_relations r
        WHERE r.relation_type IN ('direct', 'extracted')
          AND r.from_entity_id IN (SELECT entity_id FROM reachable)
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
