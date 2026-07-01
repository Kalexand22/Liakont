namespace Liakont.Modules.Ged.Tests.Integration;

using System;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Ged.Tests.Integration.Fixtures;
using Npgsql;
using Xunit;

/// <summary>
/// Tests base-réelle du schéma <c>ged_catalog</c> (GED03a, F19 §8) : ordre des migrations / FK (RL-07) et
/// journal de config append-only (<c>catalog_change_log</c>, CLAUDE.md n°4). L'arrondi half-up decimal est
/// couvert par les tests unitaires purs de <c>ValueNormalizer</c> (Domain) ; l'anti-littéral par les tests
/// unitaires de scan des migrations.
/// </summary>
[Collection("GedIntegration")]
public sealed class GedCatalogMigrationsIntegrationTests
{
    private readonly GedDatabaseFixture _fixture;

    public GedCatalogMigrationsIntegrationTests(GedDatabaseFixture fixture) => _fixture = fixture;

    // ─────────────────────────────── Ordre des migrations / FK (RL-07) ───────────────────────────────

    [Fact]
    public async Task Migrations_apply_on_a_blank_database_with_the_axis_to_entity_fk_satisfiable()
    {
        // Le fait même que CreateTenantDatabase() applique les migrations GED sur une base VIERGE prouve
        // l'ordre RL-07 (entity_types V004 AVANT axis_definitions V005) : sinon la FK fk_axis_def_target_entity
        // échouerait à la création. On le matérialise par un INSERT d'axe 'entity' pointant un type existant.
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var entityTypeId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO ged_catalog.entity_types (id, code, label) VALUES (@Id, @Code, @Label)",
            new { Id = entityTypeId, Code = "et_" + Guid.NewGuid().ToString("N"), Label = "Type" });

        Func<Task> insertAxis = () => connection.ExecuteAsync(
            "INSERT INTO ged_catalog.axis_definitions (code, label, data_type, target_entity_type_id) "
            + "VALUES (@Code, @Label, 'entity', @Target)",
            new { Code = "ax_" + Guid.NewGuid().ToString("N"), Label = "Axe", Target = entityTypeId });

        await insertAxis.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Axis_definition_entity_target_fk_is_enforced()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        Func<Task> insertDangling = () => connection.ExecuteAsync(
            "INSERT INTO ged_catalog.axis_definitions (code, label, data_type, target_entity_type_id) "
            + "VALUES (@Code, @Label, 'entity', @Target)",
            new { Code = "ax_" + Guid.NewGuid().ToString("N"), Label = "Axe", Target = Guid.NewGuid() });

        (await insertDangling.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    // ─────────────────────────────── CHECK du catalogue (jamais deviner) ───────────────────────────────

    [Fact]
    public async Task Axis_definition_rejects_a_data_type_outside_the_closed_vocabulary()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_catalog.axis_definitions (code, label, data_type) VALUES (@Code, @Label, 'decimal')",
            new { Code = "ax_" + Guid.NewGuid().ToString("N"), Label = "Axe" });

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Axis_definition_of_type_entity_requires_a_target()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_catalog.axis_definitions (code, label, data_type) VALUES (@Code, @Label, 'entity')",
            new { Code = "ax_" + Guid.NewGuid().ToString("N"), Label = "Axe" });

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Axis_definition_rejects_a_scale_out_of_range()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_catalog.axis_definitions (code, label, data_type, value_scale) "
            + "VALUES (@Code, @Label, 'number', 10)",
            new { Code = "ax_" + Guid.NewGuid().ToString("N"), Label = "Axe" });

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    // ─────────────────────────────── catalog_change_log append-only (n°4) ───────────────────────────────

    [Fact]
    public async Task Catalog_change_log_rejects_update()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var id = await InsertChangeLogAsync(connection);

        Func<Task> update = () => connection.ExecuteAsync(
            "UPDATE ged_catalog.catalog_change_log SET change_type = 'tampered' WHERE id = @Id",
            new { Id = id });

        (await update.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Catalog_change_log_rejects_delete()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var id = await InsertChangeLogAsync(connection);

        Func<Task> delete = () => connection.ExecuteAsync(
            "DELETE FROM ged_catalog.catalog_change_log WHERE id = @Id",
            new { Id = id });

        (await delete.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Catalog_change_log_rejects_truncate()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        // Le trigger d'INSTRUCTION ferme le vecteur de purge en masse même sur une table vide.
        Func<Task> truncate = () => connection.ExecuteAsync("TRUNCATE ged_catalog.catalog_change_log");

        (await truncate.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    private static async Task<Guid> InsertChangeLogAsync(System.Data.IDbConnection connection)
    {
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO ged_catalog.catalog_change_log (id, change_type) VALUES (@Id, 'axis_created')",
            new { Id = id });
        return id;
    }
}
