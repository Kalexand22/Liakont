namespace Liakont.Modules.Ingestion.Tests.Integration;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Transport;
using Liakont.Modules.Ingestion.Contracts.Commands;
using Liakont.Modules.Ingestion.Contracts.Events;
using Liakont.Modules.Ingestion.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// Invariant d'ORDRE de l'intake (PIP00, ADR-0014, INV-INGESTION-017) : le pivot complet est stagé
/// (écrit + flushé) AVANT le commit du registre + de l'événement outbox. Un échec de staging annule la
/// transaction — au pire un blob orphelin, jamais un événement sans contenu. Un doublon ne re-stage pas.
/// </summary>
[Collection("IngestionIntegration")]
public sealed class DocumentStagingIntegrationTests
{
    private readonly IngestionDatabaseFixture _fixture;

    public DocumentStagingIntegrationTests(IngestionDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Pivot_Is_Staged_Before_The_Registry_And_Event_Are_Committed()
    {
        var harness = new IngestionHarness(_fixture, NewTenant());

        int receivedVisibleAtStagingTime = -1;
        harness.PayloadStagingStore.OnWriteAsync = async key =>
        {
            // Lecture sur une connexion SÉPARÉE : la transaction d'intake n'est pas encore committée, donc
            // la ligne received_documents ne doit PAS être visible — le blob est écrit AVANT le commit.
            using var conn = await harness.ConnectionFactory.OpenAsync();
            receivedVisibleAtStagingTime = await conn.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM ingestion.received_documents WHERE tenant_id = @T",
                new { T = harness.TenantId });
        };

        await harness.BatchHandler.Handle(Batch(harness, Doc("ref-1")), CancellationToken.None);

        receivedVisibleAtStagingTime.Should().Be(0, "le blob est stagé AVANT le commit registre+outbox (INV-INGESTION-017)");
        harness.PayloadStagingStore.Count.Should().Be(1, "le contenu est stagé pour le document accepté");
        (await CountReceivedAsync(harness)).Should().Be(1);
        (await CountEventsAsync(harness, IngestionEventTypes.DocumentReceived)).Should().Be(1);
    }

    [Fact]
    public async Task Staging_Failure_Publishes_No_Event_And_No_Registry_Row()
    {
        var harness = new IngestionHarness(_fixture, NewTenant());
        harness.PayloadStagingStore.OnWriteAsync = _ => throw new IOException("disque plein (simulé)");

        Func<Task> act = () => harness.BatchHandler.Handle(Batch(harness, Doc("ref-1")), CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
        (await CountReceivedAsync(harness)).Should().Be(0, "un échec de staging annule la transaction (jamais d'événement sans contenu)");
        (await CountEventsAsync(harness, IngestionEventTypes.DocumentReceived)).Should().Be(0);
        harness.PayloadStagingStore.Count.Should().Be(0);
    }

    [Fact]
    public async Task Duplicate_Document_Is_Not_Re_Staged()
    {
        var harness = new IngestionHarness(_fixture, NewTenant());
        await harness.BatchHandler.Handle(Batch(harness, Doc("ref-1")), CancellationToken.None);

        var second = await harness.BatchHandler.Handle(Batch(harness, Doc("ref-1")), CancellationToken.None);

        second.Results.Single().Status.Should().Be(DocumentPushStatus.Duplicate);
        harness.PayloadStagingStore.WriteAttempts.Should().Be(1, "un doublon ne re-stage pas (le contenu de la 1re réception suffit)");
        harness.PayloadStagingStore.Count.Should().Be(1);
    }

    [Fact]
    public async Task Re_Push_Of_A_Ranged_Document_Re_Stages_When_Staging_Was_Lost()
    {
        // FIX07b : un document Detected (rangé) dont le contenu stagé a disparu (ex. magasin sous bin/ effacé au
        // redéploiement) ne doit PAS rester un zombie définitif. Le re-push de l'agent (filet de sécurité ADR-0014)
        // re-fournit le même contenu : on le RE-STAGE au lieu de répondre « duplicate » sans effet.
        var harness = new IngestionHarness(_fixture, NewTenant());
        await harness.BatchHandler.Handle(Batch(harness, Doc("ref-1")), CancellationToken.None);
        harness.PayloadStagingStore.Count.Should().Be(1);

        // Perte du contenu stagé (le document reste rangé/Detected en base).
        harness.PayloadStagingStore.DropAllStaged();
        harness.PayloadStagingStore.Count.Should().Be(0);

        var second = await harness.BatchHandler.Handle(Batch(harness, Doc("ref-1")), CancellationToken.None);

        second.Results.Single().Status.Should().Be(DocumentPushStatus.Duplicate);
        harness.PayloadStagingStore.Count.Should().Be(1, "le re-push d'un document rangé sans staging RE-STAGE le contenu (FIX07b) — plus de zombie définitif");
        harness.PayloadStagingStore.WriteAttempts.Should().Be(2, "1re réception + re-stage au re-push (contenu re-fourni à l'identique, empreinte connue)");

        // Pas de RÉ-ÉMISSION : la réhydratation n'écrit AUCUN nouvel événement DocumentReceived (le doublon ne
        // ré-inscrit rien). Le CHECK re-tourne sur l'événement d'ORIGINE (re-livraison outbox), jamais sur un doublon.
        (await CountReceivedAsync(harness)).Should().Be(1, "le re-push reste un doublon : aucune nouvelle inscription au registre");
        (await CountEventsAsync(harness, IngestionEventTypes.DocumentReceived)).Should().Be(1, "réhydratation du staging = aucune ré-émission d'événement");
    }

    private static IngestDocumentBatchCommand Batch(IngestionHarness harness, params PivotDocumentDto[] documents) =>
        new()
        {
            AgentId = Guid.NewGuid(),
            TenantId = harness.TenantId,
            ContractVersion = "1",
            Documents = documents,
            SourceTaxRegimes = Array.Empty<SourceTaxRegimeDto>(),
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
