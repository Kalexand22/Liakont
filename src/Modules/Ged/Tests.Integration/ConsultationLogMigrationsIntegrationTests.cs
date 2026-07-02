namespace Liakont.Modules.Ged.Tests.Integration;

using System;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Ged.Tests.Integration.Fixtures;
using Npgsql;
using Xunit;

/// <summary>
/// Tests base-réelle de la migration <c>ged_index.consultation_log</c> (GED13, F19 §6.6, ADR-0036, INV-GED-11) :
/// journal de consultation APPEND-ONLY (UPDATE/DELETE/TRUNCATE rejetés tout rôle, double trigger), vocabulaire
/// d'<c>action</c> FERMÉ (CHECK), et SOFT-LINK logique (aucune FK cross-schéma vers <c>ged_index.managed_documents</c>
/// / <c>documents.*</c> — CLAUDE.md n°9). La table vit dans la base DU TENANT (le fixture crée une base = un tenant).
/// </summary>
[Collection("GedIntegration")]
public sealed class ConsultationLogMigrationsIntegrationTests
{
    private readonly GedDatabaseFixture _fixture;

    public ConsultationLogMigrationsIntegrationTests(GedDatabaseFixture fixture) => _fixture = fixture;

    // ─────────────────────── append-only WORM (INV-GED-11, CLAUDE.md n°4) ───────────────────────

    [Fact]
    public async Task Consultation_log_rejects_update()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var id = await InsertEntryAsync(connection);

        Func<Task> update = () => connection.ExecuteAsync(
            "UPDATE ged_index.consultation_log SET query_text = 'tampered' WHERE id = @Id",
            new { Id = id });

        (await update.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Consultation_log_rejects_delete()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        var id = await InsertEntryAsync(connection);

        Func<Task> delete = () => connection.ExecuteAsync(
            "DELETE FROM ged_index.consultation_log WHERE id = @Id", new { Id = id });

        (await delete.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Consultation_log_rejects_truncate()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        // Le trigger d'INSTRUCTION ferme le vecteur de purge en masse même sur une table vide (faux-vert classique).
        Func<Task> truncate = () => connection.ExecuteAsync("TRUNCATE ged_index.consultation_log");

        (await truncate.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    // ─────────────────────── vocabulaire d'action fermé (ck_consultation_action) ───────────────────────

    [Fact]
    public async Task Consultation_log_rejects_an_action_outside_the_closed_vocabulary()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_index.consultation_log (actor_id, action) VALUES ('actor', 'bogus')");

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Theory]
    [InlineData("search")]
    [InlineData("view_document")]
    [InlineData("explore_entity")]
    [InlineData("export")]
    [InlineData("open_archive")]
    public async Task Consultation_log_accepts_each_closed_action(string action)
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_index.consultation_log (actor_id, action) VALUES ('actor', @Action)",
            new { Action = action });

        await insert.Should().NotThrowAsync();
    }

    // ─────────────────────── soft-link logique (aucune FK cross-schéma, CLAUDE.md n°9) ───────────────────────

    [Fact]
    public async Task Consultation_log_accepts_an_arbitrary_document_id_no_fk_enforced()
    {
        var factory = _fixture.CreateTenantDatabase();
        using var connection = await factory.OpenAsync();

        // managed_document_id / entity_id sont des SOFT-LINK logiques : aucune FK ne doit exister (une FK
        // cross-schéma couplerait le journal à documents.* / ged_index.managed_documents — interdit).
        Func<Task> insert = () => connection.ExecuteAsync(
            "INSERT INTO ged_index.consultation_log (actor_id, action, managed_document_id, entity_id) "
            + "VALUES ('actor', 'view_document', @Doc, @Entity)",
            new { Doc = Guid.NewGuid(), Entity = Guid.NewGuid() });

        await insert.Should().NotThrowAsync();
    }

    // ─────────────────────── Helpers ───────────────────────

    private static async Task<Guid> InsertEntryAsync(System.Data.IDbConnection connection)
    {
        return await connection.ExecuteScalarAsync<Guid>(
            "INSERT INTO ged_index.consultation_log (actor_id, action, query_text) "
            + "VALUES ('actor', 'search', 'q') RETURNING id");
    }
}
