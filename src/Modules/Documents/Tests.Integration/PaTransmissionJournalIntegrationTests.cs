namespace Liakont.Modules.Documents.Tests.Integration;

using System;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Documents.Domain.Entities;
using Liakont.Modules.Documents.Infrastructure.Queries;
using Npgsql;
using Xunit;

/// <summary>
/// Journalisation de l'envoi PA (item FX06, F16 §7) : l'extension de <c>documents.document_events</c> par les
/// colonnes additives (compte/plug-in PA, horodatages, empreinte de l'artefact, clé d'idempotence) ROUND-TRIPE
/// en base réelle, et la garantie APPEND-ONLY reste intacte sur la table étendue (CLAUDE.md n°4). Vérifié EN
/// DIRECT sur PostgreSQL — la nouvelle migration n'ouvre aucun chemin d'altération de l'audit.
/// </summary>
[Collection("DocumentsIntegration")]
public sealed class PaTransmissionJournalIntegrationTests
{
    private static readonly DateTimeOffset RequestUtc = new(2026, 6, 16, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ResponseUtc = new(2026, 6, 16, 3, 0, 2, TimeSpan.Zero);

    private readonly Fixtures.DocumentsDatabaseFixture _fixture;

    public PaTransmissionJournalIntegrationTests(Fixtures.DocumentsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Pa_Transmission_Journal_Columns_Round_Trip()
    {
        var harness = new DocumentsHarness(_fixture);
        var documentId = await SeedDocumentAsync(harness);

        var journal = NewJournalEvent(documentId);
        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            await uow.AppendEventAsync(journal);
            await uow.CommitAsync();
        }

        using var conn = await harness.ConnectionFactory.OpenAsync();
        var row = await conn.QueryFirstAsync(
            """
            SELECT event_type, pa_account, pa_plugin_id, pa_request_utc, pa_response_utc,
                   transmitted_artifact_hash, idempotency_key,
                   pa_response_snapshot::text AS pa_response_snapshot
            FROM documents.document_events
            WHERE id = @Id
            """,
            new { journal.Id });

        ((string)row.event_type).Should().Be(nameof(DocumentEventType.DocumentPaTransmissionJournaled));
        ((string)row.pa_account).Should().Be("compte-pa-demo");
        ((string)row.pa_plugin_id).Should().Be("generique");
        ((DateTime)row.pa_request_utc).Should().Be(RequestUtc.UtcDateTime);
        ((DateTime)row.pa_response_utc).Should().Be(ResponseUtc.UtcDateTime);
        ((string)row.transmitted_artifact_hash).Should().Be("sha256-de-l-artefact");
        ((string)row.idempotency_key).Should().Be("idem-key-001");
        ((string)row.pa_response_snapshot).Should().Contain("accepted", "la réponse PA est portée par la colonne existante");
    }

    [Fact]
    public async Task Update_Of_A_Journal_Event_Is_Still_Rejected()
    {
        var harness = new DocumentsHarness(_fixture);
        var documentId = await SeedDocumentAsync(harness);

        var journal = NewJournalEvent(documentId);
        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            await uow.AppendEventAsync(journal);
            await uow.CommitAsync();
        }

        using var conn = await harness.ConnectionFactory.OpenAsync();
        var update = async () => await conn.ExecuteAsync(
            "UPDATE documents.document_events SET idempotency_key = 'altéré' WHERE id = @Id",
            new { journal.Id });

        // La colonne est NOUVELLE mais la table reste append-only : aucun UPDATE n'est toléré (CLAUDE.md n°4).
        (await update.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");

        var unchanged = await conn.ExecuteScalarAsync<string>(
            "SELECT idempotency_key FROM documents.document_events WHERE id = @Id",
            new { journal.Id });
        unchanged.Should().Be("idem-key-001", "l'UPDATE a été rejeté, la valeur d'origine est intacte");
    }

    [Fact]
    public async Task Find_By_Idempotency_Key_Returns_The_Journaled_Send()
    {
        var harness = new DocumentsHarness(_fixture);
        var documentId = await SeedDocumentAsync(harness);

        // Clé UNIQUE : la base d'intégration est partagée par la collection (d'autres tests journalisent aussi),
        // donc une clé littérale partagée rendrait la recherche non déterministe (assert par clé propre, pas
        // sur une valeur globale — même discipline que les tests à fixture partagée).
        var idempotencyKey = $"idem-{Guid.NewGuid():N}";
        var journal = NewJournalEvent(documentId, idempotencyKey);
        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            await uow.AppendEventAsync(journal);
            await uow.CommitAsync();
        }

        var queries = new PostgresDocumentQueries(harness.ConnectionFactory);
        var found = await queries.FindByIdempotencyKeyAsync(idempotencyKey);

        found.Should().NotBeNull("la clé d'idempotence est recherchable (F16 §7), via l'index partiel");
        found!.DocumentId.Should().Be(documentId);
        found.EventId.Should().Be(journal.Id);
        found.IdempotencyKey.Should().Be(idempotencyKey);
        found.PaAccount.Should().Be("compte-pa-demo");
        found.PaPluginId.Should().Be("generique");
        found.TransmittedArtifactHash.Should().Be("sha256-de-l-artefact");
        found.PaRequestUtc.Should().Be(RequestUtc);
        found.PaResponseUtc.Should().Be(ResponseUtc);
    }

    [Fact]
    public async Task Find_By_Unknown_Idempotency_Key_Returns_Null()
    {
        var harness = new DocumentsHarness(_fixture);
        var queries = new PostgresDocumentQueries(harness.ConnectionFactory);

        (await queries.FindByIdempotencyKeyAsync($"absente-{Guid.NewGuid():N}")).Should().BeNull();
    }

    private static DocumentEvent NewJournalEvent(Guid documentId, string idempotencyKey = "idem-key-001") =>
        DocumentEvent.PaTransmissionJournaled(
            documentId,
            occurredAtUtc: ResponseUtc,
            paAccount: "compte-pa-demo",
            paPluginId: "generique",
            paRequestUtc: RequestUtc,
            paResponseUtc: ResponseUtc,
            transmittedArtifactHash: "sha256-de-l-artefact",
            idempotencyKey: idempotencyKey,
            paResponseSnapshot: "{\"status\":\"accepted\"}",
            detail: "Factur-X transmis via la PA générique (FX06).");

    private static async Task<Guid> SeedDocumentAsync(DocumentsHarness harness)
    {
        var document = DocumentTestData.NewDetected(documentNumber: $"AO-{Guid.NewGuid():N}");
        await using var uow = await harness.UowFactory.BeginAsync();
        await uow.CreateDetectedAsync(document, DocumentEvent.Detected(document.Id, DocumentTestData.DetectedAt));
        await uow.CommitAsync();
        return document.Id;
    }
}
