namespace Liakont.Modules.Documents.Tests.Integration;

using System;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Documents.Domain.Entities;
using Liakont.Modules.Documents.Infrastructure.Reconciliation;
using Xunit;

/// <summary>
/// Tests d'intégration du journal de rapprochement (port TRK07 implémenté par le module Documents) sur
/// PostgreSQL réel : un rapprochement automatique/manuel inscrit un <c>DocumentEvent</c> append-only sur le
/// document émis (INV-RECONCILIATION-005). Le journal est la SEULE surface autorisée pour qu'un autre
/// module écrive un fait d'audit de rapprochement (frontière Contracts-only).
/// </summary>
[Collection("DocumentsIntegration")]
public sealed class DocumentReconciliationJournalIntegrationTests
{
    private readonly DocumentsHarness _harness;

    public DocumentReconciliationJournalIntegrationTests(Fixtures.DocumentsDatabaseFixture fixture)
    {
        _harness = new DocumentsHarness(fixture);
    }

    [Fact]
    public async Task RecordsAutomaticAndManualReconciliation_AsAppendOnlyEvents()
    {
        Guid documentId = Guid.NewGuid();
        await using (var uow = await _harness.UowFactory.BeginAsync())
        {
            await uow.CreateDetectedAsync(
                DocumentTestData.NewDetected(id: documentId),
                DocumentEvent.Detected(documentId, DocumentTestData.DetectedAt));
            await uow.CommitAsync();
        }

        var journal = new DocumentReconciliationJournal(_harness.UowFactory);
        await journal.RecordAutomaticReconciliationAsync(documentId, "PDF « FAC-2026-0042.pdf » rapproché (confiance haute).");
        await journal.RecordManualReconciliationAsync(documentId, "PDF rattaché manuellement.", "operateur.test");

        using var connection = await _harness.ConnectionFactory.OpenAsync();

        long autoCount = await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM documents.document_events WHERE document_id = @Id AND event_type = 'DocumentReconciledAuto'",
            new { Id = documentId });
        autoCount.Should().Be(1);

        var manual = await connection.QuerySingleAsync(
            "SELECT event_type, operator_identity FROM documents.document_events WHERE document_id = @Id AND event_type = 'DocumentReconciledManual'",
            new { Id = documentId });
        ((string)manual.operator_identity).Should().Be("operateur.test");
    }

    [Fact]
    public async Task RecordReconciliation_OnUnknownDocument_Throws()
    {
        var journal = new DocumentReconciliationJournal(_harness.UowFactory);

        Func<Task> act = () => journal.RecordAutomaticReconciliationAsync(Guid.NewGuid(), "document inconnu");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
