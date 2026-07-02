namespace Liakont.Modules.Ged.Infrastructure.Index;

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Ged.Application;
using Liakont.Modules.Ged.Application.Index;
using Liakont.Modules.Ged.Domain.Catalog;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Implémentation PostgreSQL (tsvector) du port de recherche &amp; d'index GED (F19 §6.1-§6.4, ADR-0035), item GED08,
/// sur la base DU TENANT (isolation = la connexion, INV-GED-08). Trois responsabilités :
/// <list type="number">
/// <item><description>PROJECTION reconstructible du <c>search_vector</c> dans <c>ged_index.document_search</c>
/// (foyer unique, §6.3) — titre (poids A) + valeurs des axes searchables non confidentiels (poids B), config
/// <c>'french'</c> (D11), unaccent via le wrapper IMMUTABLE (RL-13). Ne lit QUE la base tenant.</description></item>
/// <item><description>RECHERCHE multi-axes (conjonction robuste aux axes multi-valeur, §6.2) + plein-texte + facettes,
/// keyset (RL-20), prédicat de confidentialité MATÉRIALISÉ dans le SQL (RL-31, anti-oracle).</description></item>
/// <item><description>EXPLORATION de graphe bidirectionnelle bornée (§6.4, INV-GED-09) : borne de profondeur dure,
/// anti-cycle, keyset, confidentialité héritée des <c>entity_types</c>.</description></item>
/// </list>
/// Colonnes <c>snake_case</c> aliasées (Dapper n'active pas <c>MatchNamesWithUnderscores</c> ici — sans alias la
/// propriété resterait silencieusement vide).
/// </summary>
internal sealed class PostgresDocumentSearchIndex : IDocumentSearchIndex
{
    // Le vecteur agrège : titre + doc_kind (poids A) et les valeurs d'axes SEARCHABLES et NON CONFIDENTIELS (poids B).
    // Les axes confidentiels sont EXCLUS du vecteur partagé au BUILD (INV-GED-10 : le droit confidential n'ouvre pas
    // le plein-texte en V1). unaccent via le wrapper IMMUTABLE (identique au build et à la requête). STRICT + coalesce :
    // un document sans axe searchable a un vecteur réduit au titre (jamais un tsvector NULL). No-op si le document est
    // absent (le SELECT ne rend aucune ligne) — la projection asynchrone tourne APRÈS l'indexeur (ordre garanti par le
    // dispatcher), et un replay/rebuild réécrit la ligne (UPSERT).
    private const string RefreshSql = """
        INSERT INTO ged_index.document_search (managed_document_id, search_vector, refreshed_utc)
        SELECT md.id,
               setweight(to_tsvector('french', ged_index.immutable_unaccent(
                        coalesce(md.title, '') || ' ' || coalesce(md.doc_kind, ''))), 'A')
               ||
               coalesce((
                   SELECT setweight(to_tsvector('french', ged_index.immutable_unaccent(
                               string_agg(coalesce(dal.value_string, dal.normalized_value), ' '))), 'B')
                   FROM ged_index.current_axis_links dal
                   JOIN ged_catalog.axis_definitions ad ON ad.id = dal.axis_id
                   WHERE dal.managed_document_id = md.id
                     AND ad.is_searchable = true
                     AND ad.is_confidential = false
               ), ''::tsvector),
               now()
        FROM ged_index.managed_documents md
        WHERE md.id = @Id
        ON CONFLICT (managed_document_id) DO UPDATE
            SET search_vector = EXCLUDED.search_vector,
                refreshed_utc = EXCLUDED.refreshed_utc;
        """;

    // §6.4 — traversée BIDIRECTIONNELLE bornée. Deux gardes miroir de la référence F19 : (1) la borne DURE de
    // profondeur (r.depth < @MaxDepth, clampée par l'appelant dans [0..GraphExplorationQuery.MaxAllowedDepth]=8) est
    // l'anti-DoS réel : l'ensemble de résultat est FINI, borné par le graphe du tenant restreint à cette profondeur.
    // Le tableau de CHEMIN (NOT nxt.entity_id = ANY(r.path)) garantit la TERMINAISON sur un cycle et garde chaque
    // chemin de traversée SIMPLE (aucune entité répétée dans un même chemin) ; il ne borne PAS, à lui seul, le nombre
    // de chemins simples distincts — sur un graphe tenant pathologiquement dense, le CTE récursif (UNION ALL) peut
    // matérialiser de nombreuses lignes jusqu'à la borne de profondeur ; rayon d'impact = un seul tenant, opérateur
    // authentifié ; le passage à l'échelle sur un gros corpus est le backend OpenSearch derrière IDocumentSearchIndex
    // (GED21). (2) CONFIDENTIALITÉ héritée des entity_types aux DEUX extrémités ET à la RACINE (sinon oracle
    // depth-0) — matérialisée dans le SQL (RL-31), inchangée par ce qui précède.
    // La traversée lit current_entity_relations (exclut rétractées/superséedées, RL-24) et current_document_entity_links.
    // Regroupement par (document, entité, rôle) + min(depth) : clé de résultat UNIQUE → keyset composite stable (RL-20).
    private const string ExploreSql = """
        WITH RECURSIVE reach AS (
            SELECT ei.id AS entity_id, 0 AS depth, ARRAY[ei.id]::uuid[] AS path
            FROM ged_index.entity_instances ei
            JOIN ged_catalog.entity_types et ON et.id = ei.entity_type_id
            WHERE ei.id = @Root
              AND (et.is_confidential = false OR @HasRight)
          UNION ALL
            SELECT nxt.entity_id, r.depth + 1, r.path || nxt.entity_id
            FROM reach r
            JOIN ged_index.current_entity_relations er
                 ON (er.from_entity_id = r.entity_id OR er.to_entity_id = r.entity_id)
            CROSS JOIN LATERAL (
                SELECT CASE WHEN er.from_entity_id = r.entity_id THEN er.to_entity_id ELSE er.from_entity_id END AS entity_id
            ) nxt
            JOIN ged_index.entity_instances ei ON ei.id = nxt.entity_id
            JOIN ged_catalog.entity_types et ON et.id = ei.entity_type_id
            WHERE r.depth < @MaxDepth
              AND NOT nxt.entity_id = ANY(r.path)
              AND (et.is_confidential = false OR @HasRight)
        ),
        doc_hits AS (
            SELECT del.managed_document_id AS doc_id,
                   r.entity_id            AS entity_id,
                   del.role               AS role,
                   min(r.depth)           AS depth
            FROM reach r
            JOIN ged_index.current_document_entity_links del ON del.entity_id = r.entity_id
            GROUP BY del.managed_document_id, r.entity_id, del.role
        )
        SELECT doc_id AS ManagedDocumentId, entity_id AS EntityId, role AS Role, depth AS Depth
        FROM doc_hits
        WHERE (doc_id, entity_id, role) > (@AfterDoc, @AfterEntity, @AfterRole)
        ORDER BY doc_id, entity_id, role
        LIMIT @Limit;
        """;

    private readonly IConnectionFactory _connectionFactory;
    private readonly IAxisCatalog _axisCatalog;

    public PostgresDocumentSearchIndex(IConnectionFactory connectionFactory, IAxisCatalog axisCatalog)
    {
        _connectionFactory = connectionFactory;
        _axisCatalog = axisCatalog;
    }

    public async Task RefreshDocumentAsync(Guid managedDocumentId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            RefreshSql, new { Id = managedDocumentId }, cancellationToken: cancellationToken));
    }

    public async Task<DocumentSearchResult> SearchAsync(DocumentSearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Prédicats de correspondance (conjonction) : plein-texte + filtres d'axe. Les filtres d'axe sont normalisés
        // avec le MÊME ValueNormalizer qu'à l'écriture (matche normalized_value). Un axe inconnu/inactif, une valeur
        // incompatible avec le type, ou un axe json (non searchable, INV-GED-04) rend la conjonction insatisfiable →
        // résultat VIDE (jamais deviner, règle 2). Aucun canal d'oracle : un axe confidentiel est RÉSOLU comme les
        // autres mais son critère est neutralisé par le prédicat SQL (résultat vide, indiscernable d'un axe inconnu).
        var clauses = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("HasRight", query.HasConfidentialRight);

        if (!string.IsNullOrWhiteSpace(query.FullText))
        {
            clauses.Add(
                "md.id IN (SELECT managed_document_id FROM ged_index.document_search " +
                "WHERE search_vector @@ websearch_to_tsquery('french', ged_index.immutable_unaccent(@FullText)))");
            parameters.Add("FullText", query.FullText);
        }

        if (query.AxisFilters.Count > 0)
        {
            var caseBranches = new List<string>();
            for (var i = 0; i < query.AxisFilters.Count; i++)
            {
                var filter = query.AxisFilters[i];
                var normalized = await NormalizeFilterValueAsync(filter, cancellationToken);
                if (normalized is null)
                {
                    return DocumentSearchResult.Empty;
                }

                var codeParam = "code" + i.ToString(CultureInfo.InvariantCulture);
                var valueParam = "val" + i.ToString(CultureInfo.InvariantCulture);
                var key = "k" + i.ToString(CultureInfo.InvariantCulture);
                caseBranches.Add(
                    $"WHEN ad.code = @{codeParam} AND dal.normalized_value = @{valueParam} THEN '{key}'");
                parameters.Add(codeParam, filter.AxisCode);
                parameters.Add(valueParam, normalized);
            }

            // Conjonction robuste aux axes multi-valeur (§6.2) : on compte les CRITÈRES réellement satisfaits (jamais un
            // count(DISTINCT code) naïf qui, sur un axe multi-valeur, créerait un faux positif). Le prédicat de
            // confidentialité (RL-31) filtre les liens d'axes confidentiels sans le droit → leur CASE ne s'allume jamais.
            clauses.Add(
                "md.id IN (SELECT dal.managed_document_id FROM ged_index.current_axis_links dal " +
                "JOIN ged_catalog.axis_definitions ad ON ad.id = dal.axis_id " +
                "WHERE (ad.is_confidential = false OR @HasRight) " +
                "GROUP BY dal.managed_document_id " +
                "HAVING count(DISTINCT CASE " + string.Join(" ", caseBranches) + " END) = @AxisCount)");
            parameters.Add("AxisCount", query.AxisFilters.Count);
        }

        var matchesWhere = clauses.Count > 0 ? string.Join(" AND ", clauses) : "true";
        var pageSize = Math.Clamp(query.PageSize, 1, DocumentSearchQuery.MaxPageSize);

        var hitsParameters = new DynamicParameters(parameters);
        hitsParameters.Add("After", query.AfterManagedDocumentId ?? Guid.Empty);
        hitsParameters.Add("Limit", pageSize);

        var hitsSql =
            "SELECT md.id AS ManagedDocumentId, md.title AS Title, md.doc_kind AS DocKind, md.status AS Status " +
            "FROM ged_index.managed_documents md " +
            $"WHERE ({matchesWhere}) AND md.id > @After " +
            "ORDER BY md.id LIMIT @Limit";

        // Le compte de facette = nombre de DOCUMENTS distincts portant la valeur (count DISTINCT managed_document_id),
        // jamais count(*) sur les LIGNES de liens courants : un document portant plusieurs liens courants de même
        // (axe, valeur) — cause amont GDF04-3 — gonflerait sinon le « N document(s) » affiché (GedSearchView.razor).
        var facetsSql =
            "SELECT ad.code AS AxisCode, dal.normalized_value AS Value, count(DISTINCT dal.managed_document_id) AS Count " +
            "FROM ged_index.current_axis_links dal " +
            "JOIN ged_catalog.axis_definitions ad ON ad.id = dal.axis_id " +
            "WHERE ad.is_facetable = true AND (ad.is_confidential = false OR @HasRight) " +
            "AND dal.normalized_value IS NOT NULL " +
            $"AND dal.managed_document_id IN (SELECT md.id FROM ged_index.managed_documents md WHERE ({matchesWhere})) " +
            "GROUP BY ad.code, dal.normalized_value " +
            "ORDER BY ad.code, count(DISTINCT dal.managed_document_id) DESC, dal.normalized_value";

        using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        var hits = (await connection.QueryAsync<HitRow>(new CommandDefinition(
            hitsSql, hitsParameters, cancellationToken: cancellationToken))).ToList();

        var facets = (await connection.QueryAsync<FacetRow>(new CommandDefinition(
            facetsSql, parameters, cancellationToken: cancellationToken))).ToList();

        return new DocumentSearchResult
        {
            Hits = hits.Select(h => new DocumentSearchHit
            {
                ManagedDocumentId = h.ManagedDocumentId,
                Title = h.Title,
                DocKind = h.DocKind,
                Status = h.Status,
            }).ToList(),
            Facets = facets.Select(f => new SearchFacet(f.AxisCode, f.Value, f.Count)).ToList(),
            NextCursor = hits.Count == pageSize ? hits[^1].ManagedDocumentId : null,
        };
    }

    public async Task<GraphExplorationResult> ExploreGraphAsync(GraphExplorationQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Bornes DURES appliquées côté serveur (anti-DoS) : profondeur clampée [0..MaxAllowedDepth], page [1..MaxPageSize].
        var maxDepth = Math.Clamp(query.MaxDepth, 0, GraphExplorationQuery.MaxAllowedDepth);
        var pageSize = Math.Clamp(query.PageSize, 1, GraphExplorationQuery.MaxPageSize);
        var cursor = query.After ?? new GraphCursor(Guid.Empty, Guid.Empty, string.Empty);

        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = (await connection.QueryAsync<GraphRow>(new CommandDefinition(
            ExploreSql,
            new
            {
                Root = query.RootEntityId,
                HasRight = query.HasConfidentialRight,
                MaxDepth = maxDepth,
                AfterDoc = cursor.ManagedDocumentId,
                AfterEntity = cursor.EntityId,
                AfterRole = cursor.Role,
                Limit = pageSize,
            },
            cancellationToken: cancellationToken))).ToList();

        var documents = rows.Select(r => new GraphDocumentHit
        {
            ManagedDocumentId = r.ManagedDocumentId,
            EntityId = r.EntityId,
            Role = r.Role,
            Depth = r.Depth,
        }).ToList();

        var next = rows.Count == pageSize
            ? new GraphCursor(rows[^1].ManagedDocumentId, rows[^1].EntityId, rows[^1].Role)
            : null;

        return new GraphExplorationResult { Documents = documents, NextCursor = next };
    }

    // Normalise une valeur de filtre d'axe comme à l'écriture (GED04) pour matcher normalized_value. Rend null (→ aucun
    // document) si l'axe est inconnu/inactif, la valeur incompatible avec le type, ou l'axe non searchable (json).
    private async Task<string?> NormalizeFilterValueAsync(AxisFilter filter, CancellationToken cancellationToken)
    {
        var definition = await _axisCatalog.ResolveAsync(filter.AxisCode, cancellationToken);
        if (definition is null || !definition.IsActive)
        {
            return null;
        }

        try
        {
            return ValueNormalizer.Normalize(definition.DataType, definition.ValueScale, filter.Value).NormalizedValue;
        }
        catch (AxisValueFormatException)
        {
            return null;
        }
    }

    private sealed class HitRow
    {
        public Guid ManagedDocumentId { get; init; }

        public string Title { get; init; } = string.Empty;

        public string? DocKind { get; init; }

        public string Status { get; init; } = string.Empty;
    }

    private sealed class FacetRow
    {
        public string AxisCode { get; init; } = string.Empty;

        public string Value { get; init; } = string.Empty;

        public long Count { get; init; }
    }

    private sealed class GraphRow
    {
        public Guid ManagedDocumentId { get; init; }

        public Guid EntityId { get; init; }

        public string Role { get; init; } = string.Empty;

        public int Depth { get; init; }
    }
}
