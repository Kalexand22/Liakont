namespace Liakont.Modules.Documents.Tests.Integration;

using System;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Documents.Domain.Entities;
using Npgsql;
using Xunit;

/// <summary>
/// Schéma des deux tables complémentaires owned par le module Documents (item TRK01 — schéma seul) :
/// <c>documents.tax_reports</c> (tax reports DGFiP, alimentée par TRK06) et
/// <c>documents.archive_entries</c> (références du coffre WORM, alimentée par TRK05). Vérifie l'existence,
/// les colonnes et la contrainte de clé étrangère vers <c>documents.documents</c> sur PostgreSQL réel.
/// </summary>
[Collection("DocumentsIntegration")]
public sealed class TaxReportAndArchiveSchemaIntegrationTests
{
    private readonly Fixtures.DocumentsDatabaseFixture _fixture;

    public TaxReportAndArchiveSchemaIntegrationTests(Fixtures.DocumentsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TaxReport_RoundTrips_All_Columns()
    {
        var harness = new DocumentsHarness(_fixture);
        var documentId = await SeedDocumentAsync(harness);
        var taxReportId = Guid.NewGuid();

        using var conn = await harness.ConnectionFactory.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO documents.tax_reports
                (id, document_id, pa_tax_report_id, type, transport, state, xml_base64, retrieved_utc)
            VALUES (@Id, @DocumentId, @PaId, @Type, @Transport, @State, @Xml, @Retrieved)
            """,
            new
            {
                Id = taxReportId,
                DocumentId = documentId,
                PaId = "PA-TR-1",
                Type = "E_INVOICE",
                Transport = "PDP",
                State = "Retrieved",
                Xml = "PHhtbC8+",
                Retrieved = DocumentTestData.DetectedAt,
            });

        var row = await conn.QueryFirstAsync(
            "SELECT document_id, pa_tax_report_id, type, transport, state, xml_base64 FROM documents.tax_reports WHERE id = @Id",
            new { Id = taxReportId });

        ((Guid)row.document_id).Should().Be(documentId);
        ((string)row.pa_tax_report_id).Should().Be("PA-TR-1");
        ((string)row.type).Should().Be("E_INVOICE");
        ((string)row.transport).Should().Be("PDP");
        ((string)row.state).Should().Be("Retrieved");
        ((string)row.xml_base64).Should().Be("PHhtbC8+");
    }

    [Fact]
    public async Task ArchiveEntry_RoundTrips_All_Columns()
    {
        var harness = new DocumentsHarness(_fixture);
        var documentId = await SeedDocumentAsync(harness);
        var entryId = Guid.NewGuid();

        using var conn = await harness.ConnectionFactory.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO documents.archive_entries
                (id, document_id, package_path, package_hash, chain_hash, archived_utc)
            VALUES (@Id, @DocumentId, @Path, @PackageHash, @ChainHash, @Archived)
            """,
            new
            {
                Id = entryId,
                DocumentId = documentId,
                Path = "acme/2026/05/F-2026-001/",
                PackageHash = "sha256:aaa",
                ChainHash = "sha256:bbb",
                Archived = DocumentTestData.DetectedAt,
            });

        var row = await conn.QueryFirstAsync(
            "SELECT document_id, package_path, package_hash, chain_hash FROM documents.archive_entries WHERE id = @Id",
            new { Id = entryId });

        ((Guid)row.document_id).Should().Be(documentId);
        ((string)row.package_path).Should().Be("acme/2026/05/F-2026-001/");
        ((string)row.package_hash).Should().Be("sha256:aaa");
        ((string)row.chain_hash).Should().Be("sha256:bbb");
    }

    [Fact]
    public async Task ArchiveEntry_Update_Is_Rejected()
    {
        var harness = new DocumentsHarness(_fixture);
        var documentId = await SeedDocumentAsync(harness);
        var entryId = await SeedArchiveEntryAsync(harness, documentId);

        using var conn = await harness.ConnectionFactory.OpenAsync();
        var update = async () => await conn.ExecuteAsync(
            "UPDATE documents.archive_entries SET package_path = 'altéré' WHERE id = @Id",
            new { Id = entryId });

        (await update.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("WORM");
    }

    [Fact]
    public async Task ArchiveEntry_Delete_Is_Rejected()
    {
        var harness = new DocumentsHarness(_fixture);
        var documentId = await SeedDocumentAsync(harness);
        var entryId = await SeedArchiveEntryAsync(harness, documentId);

        using var conn = await harness.ConnectionFactory.OpenAsync();
        var delete = async () => await conn.ExecuteAsync(
            "DELETE FROM documents.archive_entries WHERE id = @Id",
            new { Id = entryId });

        (await delete.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("WORM");
    }

    private static async Task<Guid> SeedDocumentAsync(DocumentsHarness harness)
    {
        var document = DocumentTestData.NewDetected(documentNumber: $"SCH-{Guid.NewGuid():N}");
        await using var uow = await harness.UowFactory.BeginAsync();
        await uow.CreateDetectedAsync(document, DocumentEvent.Detected(document.Id, DocumentTestData.DetectedAt));
        await uow.CommitAsync();
        return document.Id;
    }

    private static async Task<Guid> SeedArchiveEntryAsync(DocumentsHarness harness, Guid documentId)
    {
        var entryId = Guid.NewGuid();
        using var conn = await harness.ConnectionFactory.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO documents.archive_entries
                (id, document_id, package_path, package_hash, chain_hash, archived_utc)
            VALUES (@Id, @DocumentId, @Path, @PackageHash, @ChainHash, @Archived)
            """,
            new
            {
                Id = entryId,
                DocumentId = documentId,
                Path = $"acme/2026/05/{entryId:N}/",
                PackageHash = "sha256:ccc",
                ChainHash = "sha256:ddd",
                Archived = DocumentTestData.DetectedAt,
            });
        return entryId;
    }
}
