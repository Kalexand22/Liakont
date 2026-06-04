namespace Liakont.Modules.Reconciliation.Tests.Integration;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Reconciliation.Domain;
using Liakont.Modules.Reconciliation.Infrastructure;
using Liakont.Modules.Reconciliation.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// Tests d'intégration de la file d'attente de réconciliation sur PostgreSQL réel (item TRK07) :
/// insertion des trois catégories, lectures par état, identifiants des documents
/// rapprochés, et confirmation manuelle (mise à jour). Prouve la persistance réelle (INV-RECONCILIATION-006).
/// </summary>
[Collection("ReconciliationIntegration")]
public sealed class PostgresReconciliationQueueStoreIntegrationTests
{
    private readonly ReconciliationDatabaseFixture _fixture;

    public PostgresReconciliationQueueStoreIntegrationTests(ReconciliationDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task QueueRoundTrip_PersistsCategories_ListsAndConfirms()
    {
        TenantDatabase db = _fixture.CreateTenantDatabase();
        var store = new PostgresReconciliationQueueStore(db.ConnectionFactory);
        var now = new DateTimeOffset(2026, 1, 20, 9, 0, 0, TimeSpan.Zero);
        Guid docAuto = Guid.NewGuid();
        Guid docProposed = Guid.NewGuid();

        await store.AddAsync(ReconciliationQueueEntry.AutoReconciled("p-auto", "FAC-1.pdf", docAuto, MatchStrategy.FileName, "auto", now));
        var proposal = ReconciliationQueueEntry.PendingProposal("p-prop", "doc.pdf", docProposed, "proposition", now);
        await store.AddAsync(proposal);
        var orphan = ReconciliationQueueEntry.Orphan("p-orph", "scan.pdf", "orphelin", now);
        await store.AddAsync(orphan);

        // Recherche par PDF du pool (clé d'idempotence).
        (await store.FindByPoolPdfIdAsync("p-auto"))!.Status.Should().Be(ReconciliationStatus.ReconciledAuto);
        (await store.FindByPoolPdfIdAsync("inconnu")).Should().BeNull();

        // Lectures par état.
        (await store.ListByStatusAsync(ReconciliationStatus.PendingManual)).Should().ContainSingle()
            .Which.ProposedDocumentId.Should().Be(docProposed);
        (await store.ListByStatusAsync(ReconciliationStatus.Orphan)).Should().ContainSingle();

        // Documents rapprochés : l'auto est compté, la proposition (en attente) NON.
        (await store.ListReconciledDocumentIdsAsync()).Should().Contain(docAuto).And.NotContain(docProposed);

        // Confirmation manuelle d'un orphelin → mise à jour → ReconciledManual + compté comme rapproché.
        Guid docManual = Guid.NewGuid();
        orphan.ConfirmManually(docManual, "operateur.test", "rattachement manuel", now.AddHours(2));
        await store.UpdateAsync(orphan);

        ReconciliationQueueEntry reloaded = (await store.GetByIdAsync(orphan.Id))!;
        reloaded.Status.Should().Be(ReconciliationStatus.ReconciledManual);
        reloaded.OperatorIdentity.Should().Be("operateur.test");
        reloaded.ProposedDocumentId.Should().Be(docManual);
        (await store.ListReconciledDocumentIdsAsync()).Should().Contain(docManual);
        (await store.ListByStatusAsync(ReconciliationStatus.Orphan)).Should().BeEmpty();
    }
}
