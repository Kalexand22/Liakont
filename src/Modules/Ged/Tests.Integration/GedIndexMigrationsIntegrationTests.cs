namespace Liakont.Modules.Ged.Tests.Integration;

using System;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Ged.Tests.Integration.Fixtures;
using Npgsql;
using Xunit;

/// <summary>
/// Tests base-réelle du schéma <c>ged_index</c> documentaire (GED03b, F19 §3.4.1/§3.4.3/§8) :
/// <c>document_axis_links</c> append-only PUR (UPDATE/DELETE/TRUNCATE rejetés, révision par chaînage
/// <c>supersedes_id</c>), contrainte <c>ck_dal_value_or_retraction</c>, vue <c>current_axis_links</c> (exclut
/// rétractées ET superséedées), anti-EAV (INV-GED-01 : colonnes typées, jamais un <c>value text</c> fourre-tout ;
/// <c>value_number</c> = <c>numeric</c>/decimal, jamais double ; <c>managed_documents</c> sans <c>search_vector</c>),
/// et <c>managed_document_change_log</c> append-only (CLAUDE.md n°4).
/// </summary>
[Collection("GedIntegration")]
public sealed class GedIndexMigrationsIntegrationTests
{
    // Une colonne de valeur TYPÉE par data_type (INV-GED-01) — hissé pour CA1861 (argument de tableau constant).
    private static readonly string[] TypedValueColumns =
        ["value_string", "value_number", "value_date", "value_boolean", "value_entity_id", "value_json"];

    private readonly GedDatabaseFixture _fixture;

    public GedIndexMigrationsIntegrationTests(GedDatabaseFixture fixture) => _fixture = fixture;

    // ─────────────────────── document_axis_links append-only PUR (INV-GED-02, n°4) ───────────────────────

    [Fact]
    public async Task Document_axis_links_rejects_update()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var id = await InsertNormalLinkAsync(connection);

        Func<Task> update = () => connection.ExecuteAsync(
            "UPDATE ged_index.document_axis_links SET value_string = 'tampered' WHERE id = @Id",
            new { Id = id });

        (await update.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Document_axis_links_rejects_delete()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var id = await InsertNormalLinkAsync(connection);

        Func<Task> delete = () => connection.ExecuteAsync(
            "DELETE FROM ged_index.document_axis_links WHERE id = @Id", new { Id = id });

        (await delete.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Document_axis_links_rejects_truncate()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        // Le trigger d'INSTRUCTION ferme le vecteur de purge en masse même sur une table vide.
        Func<Task> truncate = () => connection.ExecuteAsync("TRUNCATE ged_index.document_axis_links");

        (await truncate.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    // ─────────────────────── ck_dal_value_or_retraction (§3.4.3) ───────────────────────

    [Fact]
    public async Task Normal_link_requires_exactly_one_typed_value_none_is_rejected()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        // Lien normal (is_retraction=false) SANS aucune valeur typée → 0 valeur ≠ 1 → CHECK violé.
        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_index.document_axis_links (managed_document_id, axis_id, source) "
            + "VALUES (@Doc, @Axis, 'manual')",
            new { Doc = Guid.NewGuid(), Axis = Guid.NewGuid() });

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Normal_link_with_two_typed_values_is_rejected()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        // Deux colonnes typées renseignées → 2 valeurs ≠ 1 → CHECK violé (anti-EAV : une seule valeur par lien).
        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_index.document_axis_links (managed_document_id, axis_id, value_string, value_number, source) "
            + "VALUES (@Doc, @Axis, 'x', 42, 'manual')",
            new { Doc = Guid.NewGuid(), Axis = Guid.NewGuid() });

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Retraction_without_supersedes_id_is_rejected()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        // Rétractation (is_retraction=true) sans supersedes_id → CHECK violé (une rétractation retire une valeur EXISTANTE).
        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_index.document_axis_links (managed_document_id, axis_id, source, is_retraction) "
            + "VALUES (@Doc, @Axis, 'manual', true)",
            new { Doc = Guid.NewGuid(), Axis = Guid.NewGuid() });

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Retraction_carrying_a_typed_value_is_rejected()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var superseded = await InsertNormalLinkAsync(connection);

        // Rétractation portant une valeur typée → CHECK violé (une rétractation ne porte AUCUNE valeur).
        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_index.document_axis_links (managed_document_id, axis_id, value_string, source, is_retraction, supersedes_id) "
            + "VALUES (@Doc, @Axis, 'x', 'manual', true, @Sup)",
            new { Doc = Guid.NewGuid(), Axis = Guid.NewGuid(), Sup = superseded });

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Valid_retraction_is_accepted()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var superseded = await InsertNormalLinkAsync(connection);

        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_index.document_axis_links (managed_document_id, axis_id, source, is_retraction, supersedes_id) "
            + "VALUES (@Doc, @Axis, 'manual', true, @Sup)",
            new { Doc = Guid.NewGuid(), Axis = Guid.NewGuid(), Sup = superseded });

        await insert.Should().NotThrowAsync();
    }

    // ─────────────────────── current_axis_links (rétractées/superséedées exclues, RL-24) ───────────────────────

    [Fact]
    public async Task Current_axis_links_excludes_a_superseded_row_and_keeps_the_revision()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var documentId = Guid.NewGuid();
        var axisId = Guid.NewGuid();

        // Révision PAR CHAÎNAGE (jamais d'UPDATE) : v1 puis v2 avec supersedes_id = v1.
        var v1 = await InsertNormalLinkAsync(connection, documentId, axisId, "v1");
        var v2 = await InsertNormalLinkAsync(connection, documentId, axisId, "v2", supersedes: v1);

        var current = (await connection.QueryAsync<Guid>(
            "SELECT id FROM ged_index.current_axis_links WHERE managed_document_id = @Doc",
            new { Doc = documentId })).ToList();

        current.Should().ContainSingle().Which.Should().Be(v2);
        current.Should().NotContain(v1);
    }

    [Fact]
    public async Task Current_axis_links_excludes_both_a_retracted_row_and_the_retraction()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var documentId = Guid.NewGuid();
        var axisId = Guid.NewGuid();

        var value = await InsertNormalLinkAsync(connection, documentId, axisId, "v1");
        await connection.ExecuteAsync(
            "INSERT INTO ged_index.document_axis_links (managed_document_id, axis_id, source, is_retraction, supersedes_id) "
            + "VALUES (@Doc, @Axis, 'manual', true, @Sup)",
            new { Doc = documentId, Axis = axisId, Sup = value });

        var currentCount = await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM ged_index.current_axis_links WHERE managed_document_id = @Doc",
            new { Doc = documentId });

        // La valeur est superséedée par la rétractation (exclue) ET la rétractation n'est pas courante (exclue).
        currentCount.Should().Be(0);
    }

    // ─────────────────────── managed_documents CHECK vocabulary (ck_md_status / ck_md_retention) ───────────────────────

    [Fact]
    public async Task Managed_documents_rejects_a_status_outside_the_closed_vocabulary()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_index.managed_documents (id, title, status) VALUES (@Id, 'T', 'bogus')",
            new { Id = Guid.NewGuid() });

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Managed_documents_rejects_a_retention_class_outside_the_closed_vocabulary()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_index.managed_documents (id, title, retention_class) VALUES (@Id, 'T', 'bogus')",
            new { Id = Guid.NewGuid() });

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    // ─────────────────────── Anti-EAV structurel (INV-GED-01) ───────────────────────

    [Fact]
    public async Task Document_axis_links_carries_typed_columns_and_no_catch_all_value_column()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        // Requêtes mono-colonne (aucun mapping objet Dapper — robuste vs snake_case).
        var columnNames = (await connection.QueryAsync<string>(
            "SELECT column_name FROM information_schema.columns "
            + "WHERE table_schema = 'ged_index' AND table_name = 'document_axis_links'"))
            .ToHashSet(StringComparer.Ordinal);

        // Une colonne de valeur TYPÉE par data_type (INV-GED-01) — jamais un fourre-tout.
        columnNames.Should().Contain(TypedValueColumns);
        columnNames.Should().NotContain("value", "un 'value' fourre-tout serait de l'EAV (INV-GED-01)");
        columnNames.Should().NotContain("value_text", "un 'value_text' fourre-tout serait de l'EAV (INV-GED-01)");

        // value_number est numeric (decimal exact), JAMAIS double precision / real (CLAUDE.md n°1).
        var valueNumberType = await connection.ExecuteScalarAsync<string>(
            "SELECT data_type FROM information_schema.columns "
            + "WHERE table_schema = 'ged_index' AND table_name = 'document_axis_links' AND column_name = 'value_number'");
        valueNumberType.Should().Be("numeric");
    }

    [Fact]
    public async Task Managed_documents_does_not_carry_a_search_vector_column()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        // Foyer UNIQUE du FTS document = la table dérivée document_search (GED08) ; le pivot n'en porte pas (§3.4.1).
        var searchVectorColumns = await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns "
            + "WHERE table_schema = 'ged_index' AND table_name = 'managed_documents' AND column_name = 'search_vector'");

        searchVectorColumns.Should().Be(0);
    }

    [Fact]
    public async Task Value_number_round_trips_as_an_exact_decimal()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        const decimal expected = 12345.678m;
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO ged_index.document_axis_links (id, managed_document_id, axis_id, value_number, source) "
            + "VALUES (@Id, @Doc, @Axis, @Value, 'import')",
            new { Id = id, Doc = Guid.NewGuid(), Axis = Guid.NewGuid(), Value = expected });

        var read = await connection.ExecuteScalarAsync<decimal>(
            "SELECT value_number FROM ged_index.document_axis_links WHERE id = @Id", new { Id = id });

        read.Should().Be(expected);
    }

    // ─────────────────────── managed_document_change_log append-only (n°4) ───────────────────────

    [Fact]
    public async Task Managed_document_change_log_rejects_update()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var id = await InsertChangeLogAsync(connection);

        Func<Task> update = () => connection.ExecuteAsync(
            "UPDATE ged_index.managed_document_change_log SET change_type = 'tampered' WHERE id = @Id",
            new { Id = id });

        (await update.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Managed_document_change_log_rejects_delete()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var id = await InsertChangeLogAsync(connection);

        Func<Task> delete = () => connection.ExecuteAsync(
            "DELETE FROM ged_index.managed_document_change_log WHERE id = @Id", new { Id = id });

        (await delete.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Managed_document_change_log_rejects_truncate()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        Func<Task> truncate = () => connection.ExecuteAsync("TRUNCATE ged_index.managed_document_change_log");

        (await truncate.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    // ─────────────────────── Helpers ───────────────────────

    private static async Task<Guid> InsertNormalLinkAsync(
        System.Data.IDbConnection connection,
        Guid? documentId = null,
        Guid? axisId = null,
        string value = "v",
        Guid? supersedes = null)
    {
        return await connection.ExecuteScalarAsync<Guid>(
            "INSERT INTO ged_index.document_axis_links "
            + "(managed_document_id, axis_id, value_string, source, supersedes_id) "
            + "VALUES (@Doc, @Axis, @Value, 'manual', @Sup) RETURNING id",
            new
            {
                Doc = documentId ?? Guid.NewGuid(),
                Axis = axisId ?? Guid.NewGuid(),
                Value = value,
                Sup = supersedes,
            });
    }

    private static async Task<Guid> InsertChangeLogAsync(System.Data.IDbConnection connection)
    {
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO ged_index.managed_document_change_log (id, managed_document_id, change_type) "
            + "VALUES (@Id, @Doc, 'status_changed')",
            new { Id = id, Doc = Guid.NewGuid() });
        return id;
    }
}
