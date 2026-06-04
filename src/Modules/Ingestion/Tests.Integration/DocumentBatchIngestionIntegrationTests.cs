namespace Liakont.Modules.Ingestion.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Transport;
using Liakont.Modules.Ingestion.Contracts.Commands;
using Liakont.Modules.Ingestion.Contracts.Events;
using Liakont.Modules.Ingestion.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// Réception des documents par lot (PIV04, PostgreSQL Testcontainers) : anti-doublon, altération,
/// rejet, isolation tenant, régimes source, événements outbox.
/// </summary>
[Collection("IngestionIntegration")]
public sealed class DocumentBatchIngestionIntegrationTests
{
    private readonly IngestionDatabaseFixture _fixture;

    public DocumentBatchIngestionIntegrationTests(IngestionDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task New_Documents_Are_Accepted_And_Recorded_With_Outbox_Events()
    {
        var harness = new IngestionHarness(_fixture, NewTenant());

        var response = await harness.BatchHandler.Handle(
            Batch(harness, Doc("ref-1"), Doc("ref-2")), CancellationToken.None);

        response.Results.Select(r => r.Status).Should()
            .Equal(DocumentPushStatus.Accepted, DocumentPushStatus.Accepted);
        (await CountReceivedAsync(harness)).Should().Be(2);
        (await CountEventsAsync(harness, IngestionEventTypes.DocumentReceived)).Should().Be(2);
        (await CountEventsAsync(harness, IngestionEventTypes.SourceAlterationDetected)).Should().Be(0);

        // Seuls les documents acceptés sont passés au port de création (id partagé avec la réception).
        harness.DocumentIntake.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task Re_Pushing_The_Same_Payload_Is_A_Duplicate_With_No_Effect()
    {
        var harness = new IngestionHarness(_fixture, NewTenant());
        await harness.BatchHandler.Handle(Batch(harness, Doc("ref-1")), CancellationToken.None);

        var second = await harness.BatchHandler.Handle(Batch(harness, Doc("ref-1")), CancellationToken.None);

        second.Results.Single().Status.Should().Be(DocumentPushStatus.Duplicate);
        (await CountReceivedAsync(harness)).Should().Be(1, "un payload déjà reçu n'est pas ré-inscrit.");
        (await CountEventsAsync(harness, IngestionEventTypes.DocumentReceived)).Should().Be(1);
    }

    [Fact]
    public async Task Full_Re_Push_After_Agent_Reinstall_Is_All_Duplicate()
    {
        // Un agent réinstallé perd son pushed_log local et re-pousse TOUT : la plateforme dédoublonne.
        var harness = new IngestionHarness(_fixture, NewTenant());
        var batch = new[] { Doc("ref-1"), Doc("ref-2"), Doc("ref-3") };
        await harness.BatchHandler.Handle(Batch(harness, batch), CancellationToken.None);

        var rePush = await harness.BatchHandler.Handle(Batch(harness, batch), CancellationToken.None);

        rePush.Results.Select(r => r.Status).Should()
            .OnlyContain(s => s == DocumentPushStatus.Duplicate);
        (await CountReceivedAsync(harness)).Should().Be(3);
    }

    [Fact]
    public async Task Same_Reference_Different_Payload_Is_Accepted_And_Flags_Alteration()
    {
        var harness = new IngestionHarness(_fixture, NewTenant());
        await harness.BatchHandler.Handle(Batch(harness, Doc("ref-1", number: "F-1")), CancellationToken.None);

        // Même référence source, contenu différent (numéro) → empreinte différente → altération.
        var altered = await harness.BatchHandler.Handle(
            Batch(harness, Doc("ref-1", number: "F-1-corrige")), CancellationToken.None);

        altered.Results.Single().Status.Should().Be(DocumentPushStatus.Accepted);
        (await CountReceivedAsync(harness)).Should().Be(2, "la nouvelle empreinte est inscrite à côté de l'ancienne.");
        (await CountEventsAsync(harness, IngestionEventTypes.DocumentReceived)).Should().Be(2);
        (await CountEventsAsync(harness, IngestionEventTypes.SourceAlterationDetected)).Should().Be(1);
    }

    [Fact]
    public async Task Mixed_Batch_Yields_Individual_Results()
    {
        var harness = new IngestionHarness(_fixture, NewTenant());
        await harness.BatchHandler.Handle(Batch(harness, Doc("ref-dup")), CancellationToken.None);

        var response = await harness.BatchHandler.Handle(
            Batch(harness, Doc("ref-new"), Doc("ref-dup"), Doc("ref-bad", number: string.Empty)),
            CancellationToken.None);

        response.Results.Select(r => r.Status).Should().Equal(
            DocumentPushStatus.Accepted,
            DocumentPushStatus.Duplicate,
            DocumentPushStatus.Rejected);
        response.Results[2].Reason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Malformed_Document_Is_Rejected_Without_Writing()
    {
        var harness = new IngestionHarness(_fixture, NewTenant());

        var response = await harness.BatchHandler.Handle(
            Batch(harness, Doc(sourceReference: string.Empty)), CancellationToken.None);

        response.Results.Single().Status.Should().Be(DocumentPushStatus.Rejected);
        (await CountReceivedAsync(harness)).Should().Be(0);
        harness.DocumentIntake.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Anti_Duplicate_Is_Scoped_Per_Tenant()
    {
        var tenantA = new IngestionHarness(_fixture, NewTenant());
        var tenantB = new IngestionHarness(_fixture, NewTenant());

        await tenantA.BatchHandler.Handle(Batch(tenantA, Doc("ref-1")), CancellationToken.None);

        // Le MÊME payload poussé par un autre tenant n'est PAS un doublon : anti-doublon par tenant.
        var bResult = await tenantB.BatchHandler.Handle(Batch(tenantB, Doc("ref-1")), CancellationToken.None);

        bResult.Results.Single().Status.Should().Be(DocumentPushStatus.Accepted);
        (await CountReceivedAsync(tenantA)).Should().Be(1);
        (await CountReceivedAsync(tenantB)).Should().Be(1);
    }

    [Fact]
    public async Task Source_Tax_Regimes_Are_Persisted_Per_Tenant_And_Accumulate()
    {
        var harness = new IngestionHarness(_fixture, NewTenant());

        await harness.BatchHandler.Handle(
            Batch(harness, new[] { Doc("ref-1") }, regimes: new SourceTaxRegimeDto("20", "Taux normal", 3)),
            CancellationToken.None);
        await harness.BatchHandler.Handle(
            Batch(harness, new[] { Doc("ref-2") }, regimes: new SourceTaxRegimeDto("20", "Taux normal", 2)),
            CancellationToken.None);

        var regimes = await harness.SourceTaxRegimeQueries.ListByTenantAsync(harness.TenantId);

        var normal = regimes.Single(r => r.Code == "20");
        normal.Label.Should().Be("Taux normal");
        normal.Occurrences.Should().Be(5, "les occurrences se cumulent sur les pushes.");
    }

    private static IngestDocumentBatchCommand Batch(IngestionHarness harness, params PivotDocumentDto[] documents) =>
        Batch(harness, documents, regimes: Array.Empty<SourceTaxRegimeDto>());

    private static IngestDocumentBatchCommand Batch(IngestionHarness harness, PivotDocumentDto[] documents, params SourceTaxRegimeDto[] regimes) =>
        new()
        {
            AgentId = Guid.NewGuid(),
            TenantId = harness.TenantId,
            ContractVersion = "1",
            Documents = documents,
            SourceTaxRegimes = regimes,
        };

    private static PivotDocumentDto Doc(string sourceReference, string number = "F-1", decimal ht = 100m) =>
        new(
            sourceDocumentKind: "INV",
            number: number,
            issueDate: new DateTime(2026, 3, 1),
            sourceReference: sourceReference,
            supplier: new PivotPartyDto("Fournisseur Fictif"),
            totals: new PivotTotalsDto(ht, ht * 0.2m, ht * 1.2m),
            operationCategory: OperationCategory.LivraisonBiens);

    private static async Task<int> CountReceivedAsync(IngestionHarness harness)
    {
        using var conn = await harness.ConnectionFactory.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM ingestion.received_documents WHERE tenant_id = @T",
            new { T = harness.TenantId });
    }

    private static async Task<int> CountEventsAsync(IngestionHarness harness, string eventType)
    {
        using var conn = await harness.ConnectionFactory.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM outbox.pending_events WHERE event_type = @Type AND payload->>'tenantId' = @T",
            new { Type = eventType, T = harness.TenantId });
    }

    private static string NewTenant() => "tenant-" + Guid.NewGuid().ToString("N")[..8];
}
