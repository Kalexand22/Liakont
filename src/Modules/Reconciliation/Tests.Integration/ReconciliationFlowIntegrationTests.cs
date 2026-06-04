namespace Liakont.Modules.Reconciliation.Tests.Integration;

using System;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Archive.Infrastructure;
using Liakont.Modules.Documents.Infrastructure;
using Liakont.Modules.Ingestion.Contracts;
using Liakont.Modules.Reconciliation.Contracts;
using Liakont.Modules.Reconciliation.Contracts.DTOs;
using Liakont.Modules.Reconciliation.Domain;
using Liakont.Modules.Reconciliation.Infrastructure;
using Liakont.Modules.Reconciliation.Tests.Integration.Doubles;
using Liakont.Modules.Reconciliation.Tests.Integration.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Database;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

/// <summary>
/// Flux de réconciliation BOUT-EN-BOUT sur le vrai graphe DI (modules Documents + Archive + Reconciliation)
/// et PostgreSQL réel : un PDF du pool dont le nom porte le numéro d'un document émis est rapproché
/// AUTOMATIQUEMENT — addendum d'archive RÉEL (chaîne WORM, documents.archive_entries), fait d'audit
/// append-only (DocumentReconciledAuto), entrée de file ReconciledAuto. Prouve les
/// INV-RECONCILIATION-004/005/006 dans leur intégration réelle.
/// </summary>
[Collection("ReconciliationIntegration")]
public sealed class ReconciliationFlowIntegrationTests
{
    private readonly ReconciliationDatabaseFixture _fixture;

    public ReconciliationFlowIntegrationTests(ReconciliationDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AutoLinkByFileName_AddsRealArchiveAddendum_AuditEvent_AndExcludesFromDocumentsWithoutPdf()
    {
        TenantDatabase db = _fixture.CreateTenantDatabase();
        string tenant = "tenant-" + Guid.NewGuid().ToString("N");

        Guid matchedDoc = Guid.NewGuid();
        Guid otherDoc = Guid.NewGuid();
        await SeedIssuedDocumentAsync(db, matchedDoc, "FAC-2026-0042", new DateOnly(2026, 1, 15), 1162.80m);
        await SeedIssuedDocumentAsync(db, otherDoc, "FAC-2026-0099", new DateOnly(2026, 2, 1), 50.00m);

        var pool = new InMemoryPoolStore();
        pool.Add("pool-1", "FAC-2026-0042.pdf", BuildPdf("Document de référence"));

        using ServiceProvider provider = BuildProvider(db, tenant, pool);
        using IServiceScope scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReconciliationService>();
        var queries = scope.ServiceProvider.GetRequiredService<IReconciliationQueries>();

        ReconciliationRunResult result = await service.RunForCurrentTenantAsync();

        result.AutoLinked.Should().Be(1);

        // Addendum RÉEL persisté dans la chaîne WORM (documents.archive_entries, alimentée par Archive/TRK05).
        (await CountArchiveEntriesAsync(db, matchedDoc)).Should().BeGreaterThan(0);

        // Fait d'audit append-only DocumentReconciledAuto sur le document émis.
        (await CountEventsAsync(db, matchedDoc, "DocumentReconciledAuto")).Should().Be(1);

        // File d'attente : le PDF est rapproché automatiquement.
        var queueStore = new PostgresReconciliationQueueStore(db.ConnectionFactory);
        (await queueStore.FindByPoolPdfIdAsync("pool-1"))!.Status.Should().Be(ReconciliationStatus.ReconciledAuto);

        // Documents sans PDF : le document rapproché est exclu, l'autre reste listé.
        var without = (await queries.GetIssuedDocumentsWithoutPdfAsync()).Select(d => d.DocumentId).ToList();
        without.Should().Contain(otherDoc).And.NotContain(matchedDoc);
    }

    private static ServiceProvider BuildProvider(TenantDatabase db, string tenant, IIngestedPdfStore pool)
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConnectionFactory>(db.ConnectionFactory);
        services.AddSingleton<ITenantContext>(new StubTenantContext(tenant));
        services.AddSingleton(pool);
        services.AddDocumentsModule();
        services.AddArchiveModule(configuration);
        services.AddReconciliationModule();
        return services.BuildServiceProvider();
    }

    private static async Task SeedIssuedDocumentAsync(TenantDatabase db, Guid id, string number, DateOnly issueDate, decimal totalGross)
    {
        using var connection = await db.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO documents.documents
                (id, source_reference, document_number, document_type, issue_date, supplier_siren,
                 customer_name, customer_is_company_hint, total_net, total_tax, total_gross, state,
                 payload_hash, pa_document_id, mapping_version, first_seen_utc, last_update_utc)
            VALUES
                (@Id, @SourceReference, @DocumentNumber, 'Invoice', @IssueDate, NULL,
                 NULL, FALSE, @TotalNet, 0, @TotalGross, 'Issued',
                 @PayloadHash, NULL, NULL, @Now, @Now)
            """,
            new
            {
                Id = id,
                SourceReference = "src-" + number,
                DocumentNumber = number,
                IssueDate = issueDate,
                TotalNet = totalGross,
                TotalGross = totalGross,
                PayloadHash = "hash-" + number,
                Now = DateTime.UtcNow,
            });
    }

    private static async Task<long> CountArchiveEntriesAsync(TenantDatabase db, Guid documentId)
    {
        using var connection = await db.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM documents.archive_entries WHERE document_id = @Id",
            new { Id = documentId });
    }

    private static async Task<long> CountEventsAsync(TenantDatabase db, Guid documentId, string eventType)
    {
        using var connection = await db.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM documents.document_events WHERE document_id = @Id AND event_type = @EventType",
            new { Id = documentId, EventType = eventType });
    }

    private static byte[] BuildPdf(string text)
    {
        var builder = new PdfDocumentBuilder();
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);
        PdfPageBuilder page = builder.AddPage(PageSize.A4);
        page.AddText(text, 12, new PdfPoint(25, 700), font);
        return builder.Build();
    }
}
