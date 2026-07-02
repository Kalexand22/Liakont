namespace Liakont.Modules.Ged.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Ged.Application.Index;
using Liakont.Modules.Ged.Contracts.Events;
using Liakont.Modules.Ged.Domain.Catalog;
using Liakont.Modules.Ged.Infrastructure;
using Liakont.Modules.Ged.Infrastructure.Index;
using Liakont.Modules.Ged.Tests.Integration.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Tests d'intégration de l'index de recherche GED (GED08, F19 §6.1-§6.4) sur PostgreSQL réelle (base par tenant).
/// Prouvent : projection reconstructible du <c>search_vector</c>, recherche multi-axes robuste aux axes multi-valeur,
/// plein-texte accent-insensible (unaccent IMMUTABLE), prédicat de confidentialité MATÉRIALISÉ (axe/facette/graphe,
/// anti-oracle), traversée de graphe bidirectionnelle/bornée/anti-cycle/keyset, et isolation cross-tenant.
/// </summary>
[Collection("GedIntegration")]
public sealed class DocumentSearchIndexIntegrationTests
{
    private readonly GedDatabaseFixture _fixture;

    public DocumentSearchIndexIntegrationTests(GedDatabaseFixture fixture) => _fixture = fixture;

    // ── Projection / plein-texte ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_projects_full_text_and_matches_accent_insensitively()
    {
        var factory = _fixture.CreateTenantDatabase();
        var index = NewIndex(factory);
        var axisId = await InsertAxisAsync(factory, "libelle", "string", isSearchable: true);
        var docId = await InsertManagedDocumentAsync(factory, title: "Doc neutre");
        await InsertStringAxisLinkAsync(factory, docId, axisId, "Café Déjà vu");

        await index.RefreshDocumentAsync(docId);

        // unaccent IMMUTABLE (RL-13) : « cafe » (sans accent) retrouve « Café ».
        var accentInsensitive = await index.SearchAsync(new DocumentSearchQuery { FullText = "cafe" });
        accentInsensitive.Hits.Select(h => h.ManagedDocumentId).Should().ContainSingle().Which.Should().Be(docId);

        var accented = await index.SearchAsync(new DocumentSearchQuery { FullText = "déjà" });
        accented.Hits.Should().ContainSingle(h => h.ManagedDocumentId == docId);
    }

    [Fact]
    public async Task Refresh_is_reconstructible_delete_then_rebuild()
    {
        var factory = _fixture.CreateTenantDatabase();
        var index = NewIndex(factory);
        var axisId = await InsertAxisAsync(factory, "libelle", "string", isSearchable: true);
        var docId = await InsertManagedDocumentAsync(factory, title: "Doc neutre");
        await InsertStringAxisLinkAsync(factory, docId, axisId, "reconstructible");
        await index.RefreshDocumentAsync(docId);

        // Un dérivé reconstructible : on peut TRONQUER document_search puis reprojeter (règle 4 non violée, INV-GED-07).
        await ExecuteAsync(factory, "DELETE FROM ged_index.document_search");
        (await CountSearchRowsAsync(factory)).Should().Be(0);

        await index.RefreshDocumentAsync(docId);
        (await CountSearchRowsAsync(factory)).Should().Be(1);
        var afterRebuild = await index.SearchAsync(new DocumentSearchQuery { FullText = "reconstructible" });
        afterRebuild.Hits.Should().ContainSingle(h => h.ManagedDocumentId == docId);
    }

    [Fact]
    public async Task Refresh_is_idempotent_on_replay()
    {
        var factory = _fixture.CreateTenantDatabase();
        var index = NewIndex(factory);
        var docId = await InsertManagedDocumentAsync(factory, title: "idempotent");

        await index.RefreshDocumentAsync(docId);
        await index.RefreshDocumentAsync(docId);

        (await CountSearchRowsAsync(factory)).Should().Be(1, "l'UPSERT réécrit la même ligne (livraison at-least-once)");
    }

    [Fact]
    public async Task Projector_consumer_refreshes_the_search_index_for_the_event_tenant()
    {
        var factory = _fixture.CreateTenantDatabase();
        var index = NewIndex(factory);
        var axisId = await InsertAxisAsync(factory, "libelle", "string", isSearchable: true);
        var docId = await InsertManagedDocumentAsync(factory, title: "Doc neutre");
        await InsertStringAxisLinkAsync(factory, docId, axisId, "projeté");

        const string tenantId = "tenant-ged-proj";
        var projector = new ManagedDocumentSearchProjector(
            new SingleTenantScopeFactory(tenantId, index),
            NullLogger<ManagedDocumentSearchProjector>.Instance);

        await projector.HandleAsync(BuildEvent(tenantId, docId), CancellationToken.None);

        var result = await index.SearchAsync(new DocumentSearchQuery { FullText = "projeté" });
        result.Hits.Should().ContainSingle(h => h.ManagedDocumentId == docId);
    }

    // ── Recherche multi-axes (robustesse axe multi-valeur) ────────────────────────────────────────────

    [Fact]
    public async Task Multi_axis_search_binds_code_and_value_together_no_false_positive()
    {
        var factory = _fixture.CreateTenantDatabase();
        var index = NewIndex(factory);
        var axisA = await InsertAxisAsync(factory, "axe_a", "string", isSearchable: true);
        var axisB = await InsertAxisAsync(factory, "axe_b", "string", isSearchable: true);

        // DocGood satisfait les DEUX critères ; DocBad porte les deux AXES mais une valeur de B fausse.
        // Un count(DISTINCT code) naïf compterait 2 pour DocBad (faux positif) ; le CASE code+valeur l'exclut.
        var docGood = await InsertManagedDocumentAsync(factory, title: "good");
        await InsertStringAxisLinkAsync(factory, docGood, axisA, "v1");
        await InsertStringAxisLinkAsync(factory, docGood, axisB, "v2");

        var docBad = await InsertManagedDocumentAsync(factory, title: "bad");
        await InsertStringAxisLinkAsync(factory, docBad, axisA, "v1");
        await InsertStringAxisLinkAsync(factory, docBad, axisB, "zzz");

        var result = await index.SearchAsync(new DocumentSearchQuery
        {
            AxisFilters = new[] { new AxisFilter("axe_a", "v1"), new AxisFilter("axe_b", "v2") },
        });

        result.Hits.Select(h => h.ManagedDocumentId).Should().ContainSingle().Which.Should().Be(docGood);
    }

    [Fact]
    public async Task Multi_value_axis_matches_once_without_duplicating_the_document()
    {
        var factory = _fixture.CreateTenantDatabase();
        var index = NewIndex(factory);
        var axisM = await InsertAxisAsync(factory, "tags", "string", isSearchable: true, isMultiValue: true);
        var docId = await InsertManagedDocumentAsync(factory, title: "multi");
        await InsertStringAxisLinkAsync(factory, docId, axisM, "alpha");
        await InsertStringAxisLinkAsync(factory, docId, axisM, "beta");

        var result = await index.SearchAsync(new DocumentSearchQuery
        {
            AxisFilters = new[] { new AxisFilter("tags", "alpha") },
        });

        result.Hits.Should().ContainSingle(h => h.ManagedDocumentId == docId);
    }

    [Fact]
    public async Task Search_unknown_axis_filter_returns_empty()
    {
        var factory = _fixture.CreateTenantDatabase();
        var index = NewIndex(factory);
        var docId = await InsertManagedDocumentAsync(factory, title: "doc");
        await index.RefreshDocumentAsync(docId);

        var result = await index.SearchAsync(new DocumentSearchQuery
        {
            AxisFilters = new[] { new AxisFilter("axe_inexistant", "x") },
        });

        result.Hits.Should().BeEmpty("un axe inconnu ne matche aucun document (jamais deviner, règle 2)");
    }

    // ── Confidentialité matérialisée (RL-31, anti-oracle) ─────────────────────────────────────────────

    [Fact]
    public async Task Confidential_axis_filter_is_hidden_without_right_and_revealed_with_right()
    {
        var factory = _fixture.CreateTenantDatabase();
        var index = NewIndex(factory);
        var secret = await InsertAxisAsync(factory, "axe_secret", "string", isSearchable: true, isConfidential: true);
        var docId = await InsertManagedDocumentAsync(factory, title: "doc");
        await InsertStringAxisLinkAsync(factory, docId, secret, "topsecret");

        var withoutRight = await index.SearchAsync(new DocumentSearchQuery
        {
            AxisFilters = new[] { new AxisFilter("axe_secret", "topsecret") },
            HasConfidentialRight = false,
        });
        withoutRight.Hits.Should().BeEmpty("un critère sur axe confidentiel sans le droit ne remonte rien (anti-oracle)");

        var withRight = await index.SearchAsync(new DocumentSearchQuery
        {
            AxisFilters = new[] { new AxisFilter("axe_secret", "topsecret") },
            HasConfidentialRight = true,
        });
        withRight.Hits.Should().ContainSingle(h => h.ManagedDocumentId == docId);
    }

    [Fact]
    public async Task Facets_hide_confidential_axes_without_right()
    {
        var factory = _fixture.CreateTenantDatabase();
        var index = NewIndex(factory);
        var pub = await InsertAxisAsync(factory, "axe_public", "string", isSearchable: true, isFacetable: true);
        var secret = await InsertAxisAsync(factory, "axe_secret", "string", isSearchable: true, isFacetable: true, isConfidential: true);
        var docId = await InsertManagedDocumentAsync(factory, title: "doc");
        await InsertStringAxisLinkAsync(factory, docId, pub, "pubval");
        await InsertStringAxisLinkAsync(factory, docId, secret, "secval");

        var withoutRight = await index.SearchAsync(new DocumentSearchQuery { HasConfidentialRight = false });
        withoutRight.Facets.Should().Contain(f => f.AxisCode == "axe_public");
        withoutRight.Facets.Should().NotContain(f => f.AxisCode == "axe_secret", "aucun compte confidentiel révélé (anti-oracle)");

        var withRight = await index.SearchAsync(new DocumentSearchQuery { HasConfidentialRight = true });
        withRight.Facets.Should().Contain(f => f.AxisCode == "axe_secret");
    }

    [Fact]
    public async Task Facet_count_is_distinct_documents_not_link_rows()
    {
        var factory = _fixture.CreateTenantDatabase();
        var index = NewIndex(factory);
        var axis = await InsertAxisAsync(factory, "categorie", "string", isSearchable: true, isFacetable: true, isMultiValue: true);

        // doc1 porte DEUX liens courants de MÊME valeur (cause amont GDF04-3 : doublons multi) ; doc2 en porte UN.
        // Le bucket de facette (categorie=alpha) doit compter 2 DOCUMENTS distincts, jamais 3 LIGNES de liens
        // (count(DISTINCT managed_document_id), pas count(*)).
        var doc1 = await InsertManagedDocumentAsync(factory, title: "d1");
        await InsertStringAxisLinkAsync(factory, doc1, axis, "alpha");
        await InsertStringAxisLinkAsync(factory, doc1, axis, "alpha");

        var doc2 = await InsertManagedDocumentAsync(factory, title: "d2");
        await InsertStringAxisLinkAsync(factory, doc2, axis, "alpha");

        var result = await index.SearchAsync(new DocumentSearchQuery());

        var facet = result.Facets.Should().ContainSingle(f => f.AxisCode == "categorie" && f.Value == "alpha").Subject;
        facet.Count.Should().Be(2, "deux documents distincts portent la valeur — les 3 liens ne gonflent pas le compte");
    }

    [Fact]
    public async Task Full_text_never_indexes_confidential_axis_values_even_with_right()
    {
        var factory = _fixture.CreateTenantDatabase();
        var index = NewIndex(factory);
        var secret = await InsertAxisAsync(factory, "axe_secret", "string", isSearchable: true, isConfidential: true);
        var docId = await InsertManagedDocumentAsync(factory, title: "Doc neutre");
        await InsertStringAxisLinkAsync(factory, docId, secret, "motconfidentiel");
        await index.RefreshDocumentAsync(docId);

        // INV-GED-10 : les axes confidentiels sont EXCLUS du search_vector partagé au BUILD ; le droit n'ouvre PAS le FTS en V1.
        var withRight = await index.SearchAsync(new DocumentSearchQuery { FullText = "motconfidentiel", HasConfidentialRight = true });
        withRight.Hits.Should().BeEmpty("les valeurs d'axes confidentiels ne sont jamais dans le vecteur partagé (INV-GED-10)");
    }

    // ── Pagination keyset (RL-20) ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_paginates_by_keyset_without_overlap_or_gap()
    {
        var factory = _fixture.CreateTenantDatabase();
        var index = NewIndex(factory);
        var axisId = await InsertAxisAsync(factory, "axe", "string", isSearchable: true);
        var ids = new HashSet<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var id = await InsertManagedDocumentAsync(factory, title: "doc");
            await InsertStringAxisLinkAsync(factory, id, axisId, "commun");
            ids.Add(id);
        }

        var page1 = await index.SearchAsync(new DocumentSearchQuery { AxisFilters = new[] { new AxisFilter("axe", "commun") }, PageSize = 2 });
        page1.Hits.Should().HaveCount(2);
        page1.NextCursor.Should().NotBeNull();

        var page2 = await index.SearchAsync(new DocumentSearchQuery
        {
            AxisFilters = new[] { new AxisFilter("axe", "commun") },
            PageSize = 2,
            AfterManagedDocumentId = page1.NextCursor,
        });
        page2.Hits.Should().HaveCount(1);
        page2.NextCursor.Should().BeNull();

        var seen = page1.Hits.Concat(page2.Hits).Select(h => h.ManagedDocumentId).ToList();
        seen.Should().OnlyHaveUniqueItems();
        seen.Should().BeEquivalentTo(ids);
    }

    // ── Isolation cross-tenant (≥ 2 bases) ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_is_scoped_to_the_tenant_database()
    {
        var tenantA = _fixture.CreateTenantDatabase();
        var tenantB = _fixture.CreateTenantDatabase();
        var indexA = NewIndex(tenantA);
        var indexB = NewIndex(tenantB);
        var axisA = await InsertAxisAsync(tenantA, "libelle", "string", isSearchable: true);
        var docId = await InsertManagedDocumentAsync(tenantA, title: "Doc neutre");
        await InsertStringAxisLinkAsync(tenantA, docId, axisA, "transverse");
        await indexA.RefreshDocumentAsync(docId);

        (await indexA.SearchAsync(new DocumentSearchQuery { FullText = "transverse" })).Hits.Should().ContainSingle();
        (await indexB.SearchAsync(new DocumentSearchQuery { FullText = "transverse" })).Hits
            .Should().BeEmpty("aucune donnée ne fuit vers la base du tenant B");
    }

    // ── Graphe borné bidirectionnel (§6.4, INV-GED-09) ────────────────────────────────────────────────

    [Fact]
    public async Task Graph_traversal_is_bidirectional_and_reaches_documents()
    {
        var factory = _fixture.CreateTenantDatabase();
        var index = NewIndex(factory);
        var etPublic = await InsertEntityTypeAsync(factory, "type_public", isConfidential: false);

        var root = await InsertEntityAsync(factory, etPublic, "racine");
        var forward = await InsertEntityAsync(factory, etPublic, "avant");
        var backward = await InsertEntityAsync(factory, etPublic, "arriere");

        // Une arête SORTANTE (root→forward) et une arête ENTRANTE (backward→root) : la traversée BIDIRECTIONNELLE atteint les deux.
        await InsertRelationAsync(factory, root, forward, "lie");
        await InsertRelationAsync(factory, backward, root, "lie");

        var docRoot = await InsertManagedDocumentAsync(factory, title: "d-root");
        var docFwd = await InsertManagedDocumentAsync(factory, title: "d-fwd");
        var docBwd = await InsertManagedDocumentAsync(factory, title: "d-bwd");
        await InsertDocEntityLinkAsync(factory, docRoot, root, "concerne");
        await InsertDocEntityLinkAsync(factory, docFwd, forward, "concerne");
        await InsertDocEntityLinkAsync(factory, docBwd, backward, "concerne");

        var result = await index.ExploreGraphAsync(new GraphExplorationQuery { RootEntityId = root, MaxDepth = 4 });

        result.Documents.Select(d => d.ManagedDocumentId).Should().BeEquivalentTo(new[] { docRoot, docFwd, docBwd });
    }

    [Fact]
    public async Task Graph_traversal_respects_the_depth_bound()
    {
        var factory = _fixture.CreateTenantDatabase();
        var index = NewIndex(factory);
        var et = await InsertEntityTypeAsync(factory, "type_public", isConfidential: false);
        var e0 = await InsertEntityAsync(factory, et, "e0");
        var e1 = await InsertEntityAsync(factory, et, "e1");
        var e2 = await InsertEntityAsync(factory, et, "e2");
        await InsertRelationAsync(factory, e0, e1, "lie");
        await InsertRelationAsync(factory, e1, e2, "lie");
        var d0 = await InsertManagedDocumentAsync(factory, title: "d0");
        var d1 = await InsertManagedDocumentAsync(factory, title: "d1");
        var d2 = await InsertManagedDocumentAsync(factory, title: "d2");
        await InsertDocEntityLinkAsync(factory, d0, e0, "concerne");
        await InsertDocEntityLinkAsync(factory, d1, e1, "concerne");
        await InsertDocEntityLinkAsync(factory, d2, e2, "concerne");

        var depth1 = await index.ExploreGraphAsync(new GraphExplorationQuery { RootEntityId = e0, MaxDepth = 1 });

        depth1.Documents.Select(d => d.ManagedDocumentId).Should().BeEquivalentTo(new[] { d0, d1 });
        depth1.Documents.Should().NotContain(d => d.ManagedDocumentId == d2, "e2 est à la profondeur 2, hors de la borne");
    }

    [Fact]
    public async Task Graph_traversal_terminates_on_a_cycle()
    {
        var factory = _fixture.CreateTenantDatabase();
        var index = NewIndex(factory);
        var et = await InsertEntityTypeAsync(factory, "type_public", isConfidential: false);
        var e0 = await InsertEntityAsync(factory, et, "e0");
        var e1 = await InsertEntityAsync(factory, et, "e1");
        var e2 = await InsertEntityAsync(factory, et, "e2");

        // Cycle e0 → e1 → e2 → e0.
        await InsertRelationAsync(factory, e0, e1, "lie");
        await InsertRelationAsync(factory, e1, e2, "lie");
        await InsertRelationAsync(factory, e2, e0, "lie");
        var d1 = await InsertManagedDocumentAsync(factory, title: "d1");
        await InsertDocEntityLinkAsync(factory, d1, e1, "concerne");

        // Anti-cycle par tableau de chemin : la traversée TERMINE (pas de boucle infinie) et retourne un ensemble fini.
        var result = await index.ExploreGraphAsync(new GraphExplorationQuery { RootEntityId = e0, MaxDepth = 8 });

        result.Documents.Should().ContainSingle(d => d.ManagedDocumentId == d1);
    }

    [Fact]
    public async Task Graph_traversal_excludes_confidential_entities_without_right()
    {
        var factory = _fixture.CreateTenantDatabase();
        var index = NewIndex(factory);
        var etPublic = await InsertEntityTypeAsync(factory, "type_public", isConfidential: false);
        var etSecret = await InsertEntityTypeAsync(factory, "type_secret", isConfidential: true);
        var root = await InsertEntityAsync(factory, etPublic, "racine");
        var secret = await InsertEntityAsync(factory, etSecret, "confidentiel");
        var beyond = await InsertEntityAsync(factory, etPublic, "au-dela");

        // Chaîne root → secret → beyond : sans le droit, la traversée s'arrête (secret non franchi).
        await InsertRelationAsync(factory, root, secret, "lie");
        await InsertRelationAsync(factory, secret, beyond, "lie");
        var docRoot = await InsertManagedDocumentAsync(factory, title: "d-root");
        var docSecret = await InsertManagedDocumentAsync(factory, title: "d-secret");
        var docBeyond = await InsertManagedDocumentAsync(factory, title: "d-beyond");
        await InsertDocEntityLinkAsync(factory, docRoot, root, "concerne");
        await InsertDocEntityLinkAsync(factory, docSecret, secret, "concerne");
        await InsertDocEntityLinkAsync(factory, docBeyond, beyond, "concerne");

        var withoutRight = await index.ExploreGraphAsync(new GraphExplorationQuery { RootEntityId = root, MaxDepth = 8, HasConfidentialRight = false });
        withoutRight.Documents.Select(d => d.ManagedDocumentId)
            .Should().BeEquivalentTo(new[] { docRoot }, "l'entité confidentielle n'est pas traversée sans le droit (RL-31)");

        var withRight = await index.ExploreGraphAsync(new GraphExplorationQuery { RootEntityId = root, MaxDepth = 8, HasConfidentialRight = true });
        withRight.Documents.Select(d => d.ManagedDocumentId)
            .Should().BeEquivalentTo(new[] { docRoot, docSecret, docBeyond });
    }

    [Fact]
    public async Task Graph_traversal_from_a_confidential_root_is_empty_without_right()
    {
        var factory = _fixture.CreateTenantDatabase();
        var index = NewIndex(factory);
        var etSecret = await InsertEntityTypeAsync(factory, "type_secret", isConfidential: true);
        var root = await InsertEntityAsync(factory, etSecret, "racine-confidentielle");
        var docRoot = await InsertManagedDocumentAsync(factory, title: "d-root");
        await InsertDocEntityLinkAsync(factory, docRoot, root, "concerne");

        var withoutRight = await index.ExploreGraphAsync(new GraphExplorationQuery { RootEntityId = root, MaxDepth = 4, HasConfidentialRight = false });
        withoutRight.Documents.Should().BeEmpty("une racine confidentielle sans le droit renvoie un ensemble vide (pas d'oracle depth-0)");

        var withRight = await index.ExploreGraphAsync(new GraphExplorationQuery { RootEntityId = root, MaxDepth = 4, HasConfidentialRight = true });
        withRight.Documents.Should().ContainSingle(d => d.ManagedDocumentId == docRoot);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────────

    private static PostgresDocumentSearchIndex NewIndex(IConnectionFactory factory) =>
        new(factory, new PostgresAxisCatalog(factory));

    private static async Task<Guid> InsertAxisAsync(
        IConnectionFactory factory,
        string code,
        string dataType,
        bool isSearchable = false,
        bool isFacetable = false,
        bool isConfidential = false,
        bool isMultiValue = false)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO ged_catalog.axis_definitions
                (code, label, data_type, is_multi_value, is_searchable, is_facetable, is_confidential, is_active)
            VALUES (@Code, @Code, @DataType, @Multi, @Searchable, @Facetable, @Confidential, true)
            RETURNING id
            """,
            new
            {
                Code = code,
                DataType = dataType,
                Multi = isMultiValue,
                Searchable = isSearchable,
                Facetable = isFacetable,
                Confidential = isConfidential,
            });
    }

    private static async Task<Guid> InsertManagedDocumentAsync(
        IConnectionFactory factory, string title, string status = "indexed")
    {
        var id = Guid.NewGuid();
        using var connection = await factory.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO ged_index.managed_documents (id, title, status) VALUES (@Id, @Title, @Status)",
            new { Id = id, Title = title, Status = status });
        return id;
    }

    private static async Task InsertStringAxisLinkAsync(IConnectionFactory factory, Guid docId, Guid axisId, string rawValue)
    {
        // Normalisé EXACTEMENT comme à l'écriture (GED04) → matche normalized_value côté recherche.
        var normalized = ValueNormalizer.Normalize(AxisDataType.Text, valueScale: null, rawValue).NormalizedValue;
        using var connection = await factory.OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO ged_index.document_axis_links (managed_document_id, axis_id, value_string, normalized_value, source)
            VALUES (@Doc, @Axis, @Value, @Normalized, 'manual')
            """,
            new { Doc = docId, Axis = axisId, Value = rawValue, Normalized = normalized });
    }

    private static async Task<Guid> InsertEntityTypeAsync(IConnectionFactory factory, string code, bool isConfidential)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO ged_catalog.entity_types (code, label, is_confidential, is_active)
            VALUES (@Code, @Code, @Confidential, true)
            RETURNING id
            """,
            new { Code = code, Confidential = isConfidential });
    }

    private static async Task<Guid> InsertEntityAsync(IConnectionFactory factory, Guid entityTypeId, string displayName)
    {
        var id = Guid.NewGuid();
        using var connection = await factory.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO ged_index.entity_instances (id, entity_type_id, display_name) VALUES (@Id, @Type, @Name)",
            new { Id = id, Type = entityTypeId, Name = displayName });
        return id;
    }

    private static async Task InsertRelationAsync(IConnectionFactory factory, Guid fromId, Guid toId, string relationKind)
    {
        using var connection = await factory.OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO ged_index.entity_relations (from_entity_id, to_entity_id, relation_kind, relation_type, source)
            VALUES (@From, @To, @Kind, 'direct', 'manual')
            """,
            new { From = fromId, To = toId, Kind = relationKind });
    }

    private static async Task InsertDocEntityLinkAsync(IConnectionFactory factory, Guid docId, Guid entityId, string role)
    {
        using var connection = await factory.OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO ged_index.document_entity_links (managed_document_id, entity_id, role, relation_type, source)
            VALUES (@Doc, @Entity, @Role, 'direct', 'manual')
            """,
            new { Doc = docId, Entity = entityId, Role = role });
    }

    private static async Task ExecuteAsync(IConnectionFactory factory, string sql)
    {
        using var connection = await factory.OpenAsync();
        await connection.ExecuteAsync(sql);
    }

    private static async Task<long> CountSearchRowsAsync(IConnectionFactory factory)
    {
        using var connection = await factory.OpenAsync();
        return await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM ged_index.document_search");
    }

    private static IntegrationEvent<ManagedDocumentReceivedV1> BuildEvent(string tenantId, Guid managedDocumentId) =>
        new()
        {
            EventId = Guid.NewGuid(),
            EventType = GedEventTypes.ManagedDocumentReceived,
            OccurredAt = DateTimeOffset.UnixEpoch,
            CorrelationId = Guid.NewGuid(),
            ModuleSource = "Ged",
            Version = 1,
            Payload = new ManagedDocumentReceivedV1
            {
                TenantId = tenantId,
                ManagedDocumentId = managedDocumentId,
                SourceReference = "SRC",
                PayloadHash = "hash",
                ReceivedAtUtc = DateTimeOffset.UnixEpoch,
            },
        };

    private sealed class SingleTenantScopeFactory : ITenantScopeFactory
    {
        private readonly string _tenantId;
        private readonly IDocumentSearchIndex _index;

        public SingleTenantScopeFactory(string tenantId, IDocumentSearchIndex index)
        {
            _tenantId = tenantId;
            _index = index;
        }

        public ITenantScope Create(string tenantId) => new Scope(tenantId, _index);

        private sealed class Scope : ITenantScope, IServiceProvider
        {
            private readonly IDocumentSearchIndex _index;

            public Scope(string tenantId, IDocumentSearchIndex index)
            {
                TenantId = tenantId;
                _index = index;
                Services = this;
            }

            public string TenantId { get; }

            public IServiceProvider Services { get; }

            public object? GetService(Type serviceType) =>
                serviceType == typeof(IDocumentSearchIndex) ? _index : null;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
