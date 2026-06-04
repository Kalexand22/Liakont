namespace Liakont.Modules.Documents.Tests.Integration;

using System;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Documents.Domain.Entities;
using Npgsql;
using Xunit;

/// <summary>
/// La piste d'audit <c>documents.document_events</c> est APPEND-ONLY au niveau base (CLAUDE.md n°4,
/// INV-DOCUMENTS-007) : des triggers rejettent tout UPDATE/DELETE d'une entrée et tout TRUNCATE de la
/// table. Vérifié EN DIRECT sur PostgreSQL réel — la garantie ne dépend pas du code applicatif.
/// </summary>
[Collection("DocumentsIntegration")]
public sealed class DocumentEventAppendOnlyIntegrationTests
{
    private readonly Fixtures.DocumentsDatabaseFixture _fixture;

    public DocumentEventAppendOnlyIntegrationTests(Fixtures.DocumentsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Update_Of_An_Event_Is_Rejected()
    {
        var harness = new DocumentsHarness(_fixture);
        var documentId = await SeedDocumentWithGenesisEventAsync(harness);

        using var conn = await harness.ConnectionFactory.OpenAsync();
        var update = async () => await conn.ExecuteAsync(
            "UPDATE documents.document_events SET detail = 'altéré' WHERE document_id = @d",
            new { d = documentId });

        (await update.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");

        (await EventCountAsync(harness, documentId)).Should().Be(1, "l'UPDATE a été rejeté.");
    }

    [Fact]
    public async Task Delete_Of_An_Event_Is_Rejected()
    {
        var harness = new DocumentsHarness(_fixture);
        var documentId = await SeedDocumentWithGenesisEventAsync(harness);

        using var conn = await harness.ConnectionFactory.OpenAsync();
        var delete = async () => await conn.ExecuteAsync(
            "DELETE FROM documents.document_events WHERE document_id = @d",
            new { d = documentId });

        await delete.Should().ThrowAsync<PostgresException>();
        (await EventCountAsync(harness, documentId)).Should().Be(1, "le DELETE a été rejeté.");
    }

    [Fact]
    public async Task Truncate_Of_The_Audit_Table_Is_Rejected()
    {
        var harness = new DocumentsHarness(_fixture);
        var documentId = await SeedDocumentWithGenesisEventAsync(harness);

        using var conn = await harness.ConnectionFactory.OpenAsync();
        var truncate = async () => await conn.ExecuteAsync("TRUNCATE documents.document_events");

        await truncate.Should().ThrowAsync<PostgresException>();
        (await EventCountAsync(harness, documentId)).Should().Be(1, "le TRUNCATE de masse a été rejeté.");
    }

    private static async Task<Guid> SeedDocumentWithGenesisEventAsync(DocumentsHarness harness)
    {
        var document = DocumentTestData.NewDetected(documentNumber: $"AO-{Guid.NewGuid():N}");
        await using var uow = await harness.UowFactory.BeginAsync();
        await uow.CreateDetectedAsync(document, DocumentEvent.Detected(document.Id, DocumentTestData.DetectedAt));
        await uow.CommitAsync();
        return document.Id;
    }

    private static async Task<long> EventCountAsync(DocumentsHarness harness, Guid documentId)
    {
        using var conn = await harness.ConnectionFactory.OpenAsync();
        return await conn.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM documents.document_events WHERE document_id = @d",
            new { d = documentId });
    }
}
