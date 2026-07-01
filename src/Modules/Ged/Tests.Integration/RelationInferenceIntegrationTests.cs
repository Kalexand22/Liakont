namespace Liakont.Modules.Ged.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Ged.Contracts.Commands;
using Liakont.Modules.Ged.Infrastructure;
using Liakont.Modules.Ged.Infrastructure.Graph;
using Liakont.Modules.Ged.Tests.Integration.Fixtures;
using Npgsql;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Tests base-réelle de l'inférence/héritage GED (F19 §10, GED24) : la table de config
/// <c>ged_catalog.relation_inference_rules</c> (checks mode/borne, unicité) ; et le handler bout-en-bout —
/// des relations ASSERTÉES + des règles tenant produisent des relations <c>inferred</c>/<c>inherited</c>
/// APPEND-ONLY, bornées, idempotentes.
/// </summary>
[Collection("GedIntegration")]
public sealed class RelationInferenceIntegrationTests
{
    private readonly GedDatabaseFixture _fixture;

    public RelationInferenceIntegrationTests(GedDatabaseFixture fixture) => _fixture = fixture;

    // ─────────────────────────── Config table (V018) ───────────────────────────

    [Fact]
    public async Task Rule_table_rejects_a_mode_outside_the_closed_vocabulary()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_catalog.relation_inference_rules (relation_kind, mode, max_depth) VALUES ('k', 'recursive', 3)");

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public async Task Rule_table_rejects_a_depth_outside_the_anti_dos_bound(int depth)
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_catalog.relation_inference_rules (relation_kind, mode, max_depth) VALUES ('k', 'transitive', @Depth)",
            new { Depth = depth });

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Rule_table_enforces_one_rule_per_kind_and_mode()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        await connection.ExecuteAsync(
            "INSERT INTO ged_catalog.relation_inference_rules (relation_kind, mode, max_depth) VALUES ('k', 'transitive', 3)");

        Func<Task> duplicate = () => connection.ExecuteAsync(
            "INSERT INTO ged_catalog.relation_inference_rules (relation_kind, mode, max_depth) VALUES ('k', 'transitive', 5)");

        (await duplicate.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    // ─────────────────────────── Handler bout-en-bout ───────────────────────────

    [Fact]
    public async Task Handler_infers_the_transitive_closure_and_appends_it_as_inferred()
    {
        var factory = _fixture.CreateTenantDatabase();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        using (var connection = await factory.OpenAsync())
        {
            await InsertRuleAsync(connection, "k", "transitive", 3);
            await InsertAssertedRelationAsync(connection, a, b, "k");
            await InsertAssertedRelationAsync(connection, b, c, "k");
        }

        var appended = await RunAsync(factory, a);

        appended.Should().Be(1);

        var inferred = await CurrentRelationsAsync(factory, a);
        inferred.Should().ContainSingle(r => r.ToEntity == c && r.Kind == "k" && r.RelationType == "inferred",
            "A→C est la fermeture transitive de A→B→C, provenance inferred");
    }

    [Fact]
    public async Task Handler_inherits_ancestor_relations_and_appends_them_as_inherited()
    {
        var factory = _fixture.CreateTenantDatabase();
        var child = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var target = Guid.NewGuid();

        using (var connection = await factory.OpenAsync())
        {
            await InsertRuleAsync(connection, "h", "hierarchical", 3);
            await InsertAssertedRelationAsync(connection, child, parent, "h");   // child ─h─▶ parent
            await InsertAssertedRelationAsync(connection, parent, target, "k");  // parent ─k─▶ target
        }

        var appended = await RunAsync(factory, child);

        appended.Should().Be(1);

        var inherited = await CurrentRelationsAsync(factory, child);
        inherited.Should().ContainSingle(r => r.ToEntity == target && r.Kind == "k" && r.RelationType == "inherited",
            "l'enfant hérite la relation k de son parent, provenance inherited");
    }

    [Fact]
    public async Task Handler_is_idempotent_across_runs()
    {
        var factory = _fixture.CreateTenantDatabase();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        using (var connection = await factory.OpenAsync())
        {
            await InsertRuleAsync(connection, "k", "transitive", 3);
            await InsertAssertedRelationAsync(connection, a, b, "k");
            await InsertAssertedRelationAsync(connection, b, c, "k");
        }

        var first = await RunAsync(factory, a);
        var second = await RunAsync(factory, a);

        first.Should().Be(1);
        second.Should().Be(0, "la relation dérivée est déjà courante : le second passage n'appende rien (idempotence)");

        var inferred = await CurrentRelationsAsync(factory, a);
        inferred.Count(r => r.ToEntity == c && r.Kind == "k").Should().Be(1, "une seule relation courante A→C subsiste");
    }

    [Fact]
    public async Task Handler_does_not_traverse_derived_relations_so_the_closure_converges()
    {
        // A→B→C→D assertées, borne 2 : run 1 infère A→C. Un 2ᵉ run NE DOIT PAS enchaîner A→C(inféré)→D :
        // le substrat traversé est ASSERTÉ uniquement → la fermeture converge (aucune relation A→D).
        var factory = _fixture.CreateTenantDatabase();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();

        using (var connection = await factory.OpenAsync())
        {
            await InsertRuleAsync(connection, "k", "transitive", 2);
            await InsertAssertedRelationAsync(connection, a, b, "k");
            await InsertAssertedRelationAsync(connection, b, c, "k");
            await InsertAssertedRelationAsync(connection, c, d, "k");
        }

        await RunAsync(factory, a);
        var second = await RunAsync(factory, a);

        second.Should().Be(0, "la fermeture a convergé : le 2ᵉ run n'ajoute rien");

        var current = await CurrentRelationsAsync(factory, a);
        current.Should().Contain(r => r.ToEntity == c && r.RelationType == "inferred", "A→C reste inféré");
        current.Should().NotContain(r => r.ToEntity == d, "A→D n'est jamais dérivé (borne 2 + substrat asserté, jamais via une dérivée)");
    }

    [Fact]
    public async Task Inferred_relations_are_append_only()
    {
        var factory = _fixture.CreateTenantDatabase();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        using (var connection = await factory.OpenAsync())
        {
            await InsertRuleAsync(connection, "k", "transitive", 3);
            await InsertAssertedRelationAsync(connection, a, b, "k");
            await InsertAssertedRelationAsync(connection, b, c, "k");
        }

        await RunAsync(factory, a);

        using var conn = await factory.OpenAsync();
        var id = await conn.QuerySingleAsync<Guid>(
            "SELECT id FROM ged_index.entity_relations WHERE relation_type = 'inferred' LIMIT 1");

        Func<Task> update = () => conn.ExecuteAsync(
            "UPDATE ged_index.entity_relations SET relation_kind = 'tampered' WHERE id = @Id", new { Id = id });

        (await update.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    // ─────────────────────────── Helpers ───────────────────────────

    private static Task<int> RunAsync(IConnectionFactory factory, Guid seed)
    {
        var handler = new InferEntityRelationsCommandHandler(
            new PostgresRelationInferenceRuleStore(factory),
            new PostgresEntityRelationGraphReader(factory),
            new PostgresGedIndexUnitOfWorkFactory(factory));

        return handler.Handle(new InferEntityRelationsCommand { SeedEntityId = seed, Source = "agent" }, default);
    }

    private static Task<int> InsertRuleAsync(System.Data.IDbConnection connection, string kind, string mode, int maxDepth) =>
        connection.ExecuteAsync(
            "INSERT INTO ged_catalog.relation_inference_rules (relation_kind, mode, max_depth) VALUES (@Kind, @Mode, @Depth)",
            new { Kind = kind, Mode = mode, Depth = maxDepth });

    private static Task<int> InsertAssertedRelationAsync(System.Data.IDbConnection connection, Guid from, Guid to, string kind) =>
        connection.ExecuteAsync(
            "INSERT INTO ged_index.entity_relations (from_entity_id, to_entity_id, relation_kind, relation_type, source) "
            + "VALUES (@From, @To, @Kind, 'direct', 'manual')",
            new { From = from, To = to, Kind = kind });

    private static async Task<List<CurrentRelationRow>> CurrentRelationsAsync(IConnectionFactory factory, Guid from)
    {
        using var connection = await factory.OpenAsync();
        var rows = await connection.QueryAsync<CurrentRelationRow>(
            "SELECT to_entity_id AS ToEntity, relation_kind AS Kind, relation_type AS RelationType "
            + "FROM ged_index.current_entity_relations WHERE from_entity_id = @From",
            new { From = from });
        return rows.ToList();
    }

    private sealed class CurrentRelationRow
    {
        public Guid ToEntity { get; set; }

        public string Kind { get; set; } = string.Empty;

        public string RelationType { get; set; } = string.Empty;
    }
}
