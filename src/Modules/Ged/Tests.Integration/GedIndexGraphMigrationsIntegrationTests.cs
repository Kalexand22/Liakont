namespace Liakont.Modules.Ged.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Ged.Tests.Integration.Fixtures;
using Npgsql;
using Xunit;

/// <summary>
/// Tests base-réelle du graphe <c>ged_index</c> livré par GED03c (F19 §3.4.2/§3.4.4/§3.4.5, §8) :
/// append-only des tables de liens graphe (<c>entity_relations</c>, <c>document_entity_links</c>) et du
/// journal d'instances (<c>entity_instance_change_log</c>) — CLAUDE.md n°4 ; vues <c>current_*</c> qui
/// excluent les lignes rétractées ET superséedées (RL-24) ; <c>ck_er_no_self</c> ; rétractation append-only
/// (y compris multi-valeur) ; et INV-GED-04 (<c>attributes</c> jsonb présentation-only, jamais indexé).
/// Le méta-modèle est GÉNÉRIQUE : aucun type d'entité / rôle métier n'est codé en dur (garde anti-littéral
/// portée par les tests unitaires de scan des migrations).
/// </summary>
[Collection("GedIntegration")]
public sealed class GedIndexGraphMigrationsIntegrationTests
{
    private readonly GedDatabaseFixture _fixture;

    public GedIndexGraphMigrationsIntegrationTests(GedDatabaseFixture fixture) => _fixture = fixture;

    // ─────────────────────────── entity_relations append-only (n°4) ───────────────────────────

    [Fact]
    public async Task Entity_relations_reject_update()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var id = await InsertRelationAsync(connection, Guid.NewGuid(), Guid.NewGuid());

        Func<Task> update = () => connection.ExecuteAsync(
            "UPDATE ged_index.entity_relations SET relation_kind = 'tampered' WHERE id = @Id",
            new { Id = id });

        (await update.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Entity_relations_reject_delete()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var id = await InsertRelationAsync(connection, Guid.NewGuid(), Guid.NewGuid());

        Func<Task> delete = () => connection.ExecuteAsync(
            "DELETE FROM ged_index.entity_relations WHERE id = @Id",
            new { Id = id });

        (await delete.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Entity_relations_reject_truncate()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        // Le trigger d'INSTRUCTION ferme le vecteur de purge en masse même sur une table vide.
        Func<Task> truncate = () => connection.ExecuteAsync("TRUNCATE ged_index.entity_relations");

        (await truncate.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Entity_relations_reject_a_self_relation()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var same = Guid.NewGuid();
        Func<Task> insertSelf = () => InsertRelationAsync(connection, same, same);

        (await insertSelf.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Entity_relations_reject_a_source_outside_the_closed_vocabulary()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_index.entity_relations (from_entity_id, to_entity_id, relation_kind, relation_type, source) "
            + "VALUES (@From, @To, 'k', 'direct', 'bogus')",
            new { From = Guid.NewGuid(), To = Guid.NewGuid() });

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Entity_relations_reject_a_relation_type_outside_the_closed_vocabulary()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_index.entity_relations (from_entity_id, to_entity_id, relation_kind, relation_type, source) "
            + "VALUES (@From, @To, 'k', 'bogus', 'manual')",
            new { From = Guid.NewGuid(), To = Guid.NewGuid() });

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Entity_relations_reject_a_confidence_score_out_of_range()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_index.entity_relations (from_entity_id, to_entity_id, relation_kind, relation_type, source, confidence_score) "
            + "VALUES (@From, @To, 'k', 'direct', 'manual', 2)",
            new { From = Guid.NewGuid(), To = Guid.NewGuid() });

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Entity_relations_retraction_requires_a_supersedes_target()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        // is_retraction=true sans supersedes_id viole ck_er_retraction : une rétractation DÉSIGNE ce qu'elle retire.
        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_index.entity_relations "
            + "(from_entity_id, to_entity_id, relation_kind, relation_type, source, is_retraction) "
            + "VALUES (@From, @To, 'k', 'direct', 'manual', true)",
            new { From = Guid.NewGuid(), To = Guid.NewGuid() });

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Current_entity_relations_excludes_superseded_rows()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var original = await InsertRelationAsync(connection, from, to);
        var revision = await InsertRelationAsync(connection, from, to, supersedesId: original);

        var current = (await connection.QueryAsync<Guid>(
            "SELECT id FROM ged_index.current_entity_relations")).ToList();

        current.Should().Contain(revision).And.NotContain(original,
            "la vue current_entity_relations expose la dernière ligne de la chaîne, pas la superséedée");
    }

    [Fact]
    public async Task Current_entity_relations_excludes_retracted_rows_without_any_update_or_delete()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var original = await InsertRelationAsync(connection, from, to);

        // Rétractation append-only : nouvelle ligne is_retraction=true (aucun UPDATE/DELETE, RL-24).
        await InsertRelationAsync(connection, from, to, supersedesId: original, isRetraction: true);

        var current = (await connection.QueryAsync<Guid>(
            "SELECT id FROM ged_index.current_entity_relations")).ToList();

        current.Should().NotContain(original,
            "la ligne originale est superséedée par la rétractation ; la rétractation elle-même est is_retraction=true donc hors current");
        current.Should().BeEmpty("aucune relation courante ne subsiste après rétractation de l'unique lien");
    }

    [Fact]
    public async Task Current_entity_relations_retraction_is_scoped_to_the_targeted_relation_when_multi_valued()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        // Multi-valeur (RL-24) : deux relations courantes distinctes entre les mêmes entités (kinds différents).
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var kept = await InsertRelationAsync(connection, from, to, relationKind: "kind_a");
        var retractedTarget = await InsertRelationAsync(connection, from, to, relationKind: "kind_b");

        // On ne rétracte QUE la seconde : la première reste courante.
        await InsertRelationAsync(connection, from, to, relationKind: "kind_b",
            supersedesId: retractedTarget, isRetraction: true);

        var current = (await connection.QueryAsync<Guid>(
            "SELECT id FROM ged_index.current_entity_relations")).ToList();

        current.Should().Contain(kept).And.NotContain(retractedTarget,
            "la rétractation d'une valeur ne retire que la ligne visée ; les autres valeurs courantes subsistent");
    }

    // ─────────────────────────── document_entity_links append-only (n°4) ───────────────────────────

    [Fact]
    public async Task Document_entity_links_reject_update()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var id = await InsertDocumentEntityLinkAsync(connection, Guid.NewGuid(), Guid.NewGuid());

        Func<Task> update = () => connection.ExecuteAsync(
            "UPDATE ged_index.document_entity_links SET role = 'tampered' WHERE id = @Id",
            new { Id = id });

        (await update.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Document_entity_links_reject_delete()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var id = await InsertDocumentEntityLinkAsync(connection, Guid.NewGuid(), Guid.NewGuid());

        Func<Task> delete = () => connection.ExecuteAsync(
            "DELETE FROM ged_index.document_entity_links WHERE id = @Id",
            new { Id = id });

        (await delete.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Document_entity_links_reject_truncate()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        Func<Task> truncate = () => connection.ExecuteAsync("TRUNCATE ged_index.document_entity_links");

        (await truncate.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Document_entity_links_retraction_requires_a_supersedes_target()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_index.document_entity_links "
            + "(managed_document_id, entity_id, role, relation_type, source, is_retraction) "
            + "VALUES (@Doc, @Entity, 'r', 'direct', 'manual', true)",
            new { Doc = Guid.NewGuid(), Entity = Guid.NewGuid() });

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Document_entity_links_reject_a_source_outside_the_closed_vocabulary()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_index.document_entity_links (managed_document_id, entity_id, role, relation_type, source) "
            + "VALUES (@Doc, @Entity, 'r', 'direct', 'bogus')",
            new { Doc = Guid.NewGuid(), Entity = Guid.NewGuid() });

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Document_entity_links_reject_a_relation_type_outside_the_closed_vocabulary()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_index.document_entity_links (managed_document_id, entity_id, role, relation_type, source) "
            + "VALUES (@Doc, @Entity, 'r', 'bogus', 'manual')",
            new { Doc = Guid.NewGuid(), Entity = Guid.NewGuid() });

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Document_entity_links_reject_a_confidence_score_out_of_range()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_index.document_entity_links (managed_document_id, entity_id, role, relation_type, source, confidence_score) "
            + "VALUES (@Doc, @Entity, 'r', 'direct', 'manual', 2)",
            new { Doc = Guid.NewGuid(), Entity = Guid.NewGuid() });

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Current_document_entity_links_excludes_superseded_rows()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var doc = Guid.NewGuid();
        var entity = Guid.NewGuid();
        var original = await InsertDocumentEntityLinkAsync(connection, doc, entity);
        var revision = await InsertDocumentEntityLinkAsync(connection, doc, entity, supersedesId: original);

        var current = (await connection.QueryAsync<Guid>(
            "SELECT id FROM ged_index.current_document_entity_links")).ToList();

        current.Should().Contain(revision).And.NotContain(original);
    }

    [Fact]
    public async Task Current_document_entity_links_excludes_retracted_rows_without_any_update_or_delete()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var doc = Guid.NewGuid();
        var entity = Guid.NewGuid();
        var original = await InsertDocumentEntityLinkAsync(connection, doc, entity);
        await InsertDocumentEntityLinkAsync(connection, doc, entity, supersedesId: original, isRetraction: true);

        var current = (await connection.QueryAsync<Guid>(
            "SELECT id FROM ged_index.current_document_entity_links")).ToList();

        current.Should().NotContain(original);
        current.Should().BeEmpty();
    }

    [Fact]
    public async Task Current_document_entity_links_retraction_is_scoped_to_the_targeted_link_when_multi_valued()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        // Multi-valeur (RL-24) : deux liens courants distincts entre le même doc et la même entité (rôles différents).
        var doc = Guid.NewGuid();
        var entity = Guid.NewGuid();
        var kept = await InsertDocumentEntityLinkAsync(connection, doc, entity, role: "role_a");
        var retractedTarget = await InsertDocumentEntityLinkAsync(connection, doc, entity, role: "role_b");

        // On ne rétracte QUE le second : le premier reste courant.
        await InsertDocumentEntityLinkAsync(connection, doc, entity, role: "role_b",
            supersedesId: retractedTarget, isRetraction: true);

        var current = (await connection.QueryAsync<Guid>(
            "SELECT id FROM ged_index.current_document_entity_links")).ToList();

        current.Should().Contain(kept).And.NotContain(retractedTarget,
            "la rétractation d'un lien ne retire que la ligne visée ; les autres liens courants subsistent");
    }

    // ─────────────────────────── entity_instance_change_log append-only (n°4) ───────────────────────────

    [Fact]
    public async Task Entity_instance_change_log_rejects_update()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var id = await InsertEntityInstanceChangeLogAsync(connection);

        Func<Task> update = () => connection.ExecuteAsync(
            "UPDATE ged_index.entity_instance_change_log SET change_type = 'tampered' WHERE id = @Id",
            new { Id = id });

        (await update.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Entity_instance_change_log_rejects_delete()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var id = await InsertEntityInstanceChangeLogAsync(connection);

        Func<Task> delete = () => connection.ExecuteAsync(
            "DELETE FROM ged_index.entity_instance_change_log WHERE id = @Id",
            new { Id = id });

        (await delete.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Entity_instance_change_log_rejects_truncate()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        Func<Task> truncate = () => connection.ExecuteAsync("TRUNCATE ged_index.entity_instance_change_log");

        (await truncate.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    // ─────────────────────────── entity_instances MUTABLE + INV-GED-04 ───────────────────────────

    [Fact]
    public async Task Entity_instances_allow_update_because_the_registry_is_mutable_and_journaled_separately()
    {
        // Contraste avec les tables de liens : entity_instances n'est PAS append-only (registre vivant :
        // fusion de doublons, désactivation logique). Ses mutations sont tracées dans entity_instance_change_log.
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var id = await InsertEntityInstanceAsync(connection);

        Func<Task> update = () => connection.ExecuteAsync(
            "UPDATE ged_index.entity_instances SET display_name = 'renamed', updated_utc = now() WHERE id = @Id",
            new { Id = id });

        await update.Should().NotThrowAsync("entity_instances est mutable (registre vivant), pas append-only");
    }

    [Fact]
    public async Task Entity_instances_attributes_column_is_not_indexed_INV_GED_04()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var indexDefs = (await connection.QueryAsync<string>(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'ged_index' AND tablename = 'entity_instances'"))
            .ToList();

        indexDefs.Should().NotBeEmpty("entity_instances porte au moins ses index de type/identité/recherche");
        indexDefs.Should().NotContain(def => def.Contains("attributes", StringComparison.OrdinalIgnoreCase),
            "INV-GED-04 : `attributes` jsonb est présentation-only — jamais indexé (aucun canal de recherche/facette)");
    }

    [Fact]
    public async Task Entity_instances_search_vector_has_a_gin_index()
    {
        // Le SEUL canal interrogeable d'une entité est search_vector (INV-GED-04) : on prouve qu'il EXISTE
        // (sinon INV-GED-04 serait vrai par vacuité — aucun index du tout).
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var indexDefs = (await connection.QueryAsync<string>(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'ged_index' AND tablename = 'entity_instances'"))
            .ToList();

        indexDefs.Should().Contain(
            def => def.Contains("gin", StringComparison.OrdinalIgnoreCase)
                && def.Contains("search_vector", StringComparison.OrdinalIgnoreCase),
            "search_vector porte un index GIN — l'unique canal de recherche plein-texte d'entité");
    }

    // ─────────────────────────── Helpers ───────────────────────────

    private static Task<Guid> InsertRelationAsync(
        System.Data.IDbConnection connection,
        Guid from,
        Guid to,
        string relationKind = "k",
        Guid? supersedesId = null,
        bool isRetraction = false) =>
        connection.QuerySingleAsync<Guid>(
            "INSERT INTO ged_index.entity_relations "
            + "(from_entity_id, to_entity_id, relation_kind, relation_type, source, supersedes_id, is_retraction) "
            + "VALUES (@From, @To, @Kind, 'direct', 'manual', @Supersedes, @IsRetraction) RETURNING id",
            new { From = from, To = to, Kind = relationKind, Supersedes = supersedesId, IsRetraction = isRetraction });

    private static Task<Guid> InsertDocumentEntityLinkAsync(
        System.Data.IDbConnection connection,
        Guid document,
        Guid entity,
        string role = "r",
        Guid? supersedesId = null,
        bool isRetraction = false) =>
        connection.QuerySingleAsync<Guid>(
            "INSERT INTO ged_index.document_entity_links "
            + "(managed_document_id, entity_id, role, relation_type, source, supersedes_id, is_retraction) "
            + "VALUES (@Doc, @Entity, @Role, 'direct', 'manual', @Supersedes, @IsRetraction) RETURNING id",
            new { Doc = document, Entity = entity, Role = role, Supersedes = supersedesId, IsRetraction = isRetraction });

    private static Task<Guid> InsertEntityInstanceAsync(System.Data.IDbConnection connection) =>
        connection.QuerySingleAsync<Guid>(
            "INSERT INTO ged_index.entity_instances (entity_type_id, display_name) "
            + "VALUES (@TypeId, @Name) RETURNING id",
            new { TypeId = Guid.NewGuid(), Name = "instance_" + Guid.NewGuid().ToString("N") });

    private static Task<Guid> InsertEntityInstanceChangeLogAsync(System.Data.IDbConnection connection) =>
        connection.QuerySingleAsync<Guid>(
            "INSERT INTO ged_index.entity_instance_change_log (change_type) VALUES ('entity_created') RETURNING id");
}
