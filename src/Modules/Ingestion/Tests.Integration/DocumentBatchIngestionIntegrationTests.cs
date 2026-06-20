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
    public async Task Received_But_Not_Ranged_Document_Is_Re_Ranged_On_Re_Push()
    {
        // AFFINAGE DÉDOUBLONNAGE (ADR-0012) : ferme la perte silencieuse d'un document reçu mais jamais rangé.
        var harness = new IngestionHarness(_fixture, NewTenant());

        // 1er push : la réception réussit (committée) mais le RANGEMENT échoue (hoquet de la base tenant simulé).
        harness.DocumentIntake.FailNextRegistrations(1);
        var first = await harness.BatchHandler.Handle(Batch(harness, Doc("ref-1")), CancellationToken.None);

        first.Results.Single().Status.Should().Be(DocumentPushStatus.Accepted);
        (await CountReceivedAsync(harness)).Should().Be(1, "le document est reçu (inscrit au registre).");
        harness.DocumentIntake.Calls.Should().HaveCount(1, "le rangement a été tenté une fois (et a échoué).");
        var documentId = harness.DocumentIntake.Calls.Single().DocumentId;
        (await harness.DocumentIntake.IsDocumentRangedAsync(documentId, harness.TenantId))
            .Should().BeFalse("le rangement a échoué : reçu mais NON rangé (la fuite à fermer).");

        // Renvoi du MÊME payload (filet de sécurité de l'agent) : doublon, MAIS le rangement est RE-TENTÉ.
        var rePush = await harness.BatchHandler.Handle(Batch(harness, Doc("ref-1")), CancellationToken.None);

        rePush.Results.Single().Status.Should().Be(DocumentPushStatus.Duplicate);
        (await CountReceivedAsync(harness)).Should().Be(1, "le registre de réception reste append-only (aucune ré-inscription).");
        harness.DocumentIntake.Calls.Should().HaveCount(2, "le renvoi re-tente le rangement (idempotent), jamais écarté aveuglément.");
        harness.DocumentIntake.Calls.Select(c => c.DocumentId).Distinct().Should()
            .ContainSingle().Which.Should().Be(documentId, "le re-rangement réutilise l'identité de la réception d'origine.");
        (await harness.DocumentIntake.IsDocumentRangedAsync(documentId, harness.TenantId))
            .Should().BeTrue("le document est désormais rangé — pas perdu.");
        harness.PayloadStagingStore.Count.Should().Be(1, "le pivot est (re)stagé pour le pipeline (filet ADR-0014).");
    }

    [Fact]
    public async Task Re_Push_Of_Already_Ranged_Document_Does_Not_Re_Range()
    {
        // Un vrai doublon (document DÉJÀ rangé) reste terminal — aucun re-rangement inutile (ADR-0012).
        var harness = new IngestionHarness(_fixture, NewTenant());
        await harness.BatchHandler.Handle(Batch(harness, Doc("ref-1")), CancellationToken.None);
        harness.DocumentIntake.Calls.Should().HaveCount(1, "le rangement réussit du premier coup.");

        var rePush = await harness.BatchHandler.Handle(Batch(harness, Doc("ref-1")), CancellationToken.None);

        rePush.Results.Single().Status.Should().Be(DocumentPushStatus.Duplicate);
        harness.DocumentIntake.Calls.Should().HaveCount(1, "un document déjà rangé n'est pas re-rangé (terminal).");
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
    public async Task Source_Tax_Regimes_Are_Persisted_Per_Tenant_With_Idempotent_Last_Observation()
    {
        var harness = new IngestionHarness(_fixture, NewTenant());

        await harness.BatchHandler.Handle(
            Batch(harness, new[] { Doc("ref-1") }, regimes: new SourceTaxRegimeDto("20", "Taux normal", 3)),
            CancellationToken.None);
        await harness.BatchHandler.Handle(
            Batch(harness, new[] { Doc("ref-2") }, regimes: new SourceTaxRegimeDto("20", "Taux normal", 5)),
            CancellationToken.None);

        // Rejeu d'un même push (retry réseau) : NE doit PAS gonfler les occurrences (idempotent).
        await harness.BatchHandler.Handle(
            Batch(harness, new[] { Doc("ref-3") }, regimes: new SourceTaxRegimeDto("20", "Taux normal", 5)),
            CancellationToken.None);

        var regimes = await harness.SourceTaxRegimeQueries.ListByTenantAsync(harness.TenantId);

        var normal = regimes.Single(r => r.Code == "20");
        normal.Label.Should().Be("Taux normal");
        normal.Occurrences.Should().Be(5, "occurrences = dernière observation (remplacée), jamais cumulée → idempotent.");
    }

    [Fact]
    public async Task Extractor_Capabilities_Are_Transported_Persisted_Per_Agent_And_Read_Back()
    {
        // Round-trip RD401 : transport (PushBatchRequestDto → commande) → persistance (par agent/tenant)
        // → relecture par la plateforme. Capacités DÉCLARÉES, jamais interprétées ici.
        var harness = new IngestionHarness(_fixture, NewTenant());
        var agentId = Guid.NewGuid();
        var otherAgentId = Guid.NewGuid();

        var capabilities = new ExtractorCapabilitiesDto(
            providesSourceDocuments: true,
            providesUnlinkedDocumentPool: false,
            hasDetailedLines: true,
            hasCreditNoteLink: true,
            exposesPayments: true,
            regimeKeyShape: "Composite",
            emitterIdentitySource: "InBase",
            hasStoredHeaderTotal: true,
            isMutableAfterIssue: true,
            numberUniquenessScope: "PerEstablishment");

        await harness.BatchHandler.Handle(
            BatchWithCapabilities(harness, agentId, capabilities, Doc("ref-1")), CancellationToken.None);

        var read = await harness.ExtractorCapabilitiesQueries.GetByAgentAsync(harness.TenantId, agentId);

        read.Should().NotBeNull();
        read!.ProvidesSourceDocuments.Should().BeTrue();
        read.ProvidesUnlinkedDocumentPool.Should().BeFalse();
        read.HasDetailedLines.Should().BeTrue();
        read.HasCreditNoteLink.Should().BeTrue();
        read.ExposesPayments.Should().BeTrue();
        read.RegimeKeyShape.Should().Be("Composite");
        read.EmitterIdentitySource.Should().Be("InBase");
        read.HasStoredHeaderTotal.Should().BeTrue();
        read.IsMutableAfterIssue.Should().BeTrue();
        read.NumberUniquenessScope.Should().Be("PerEstablishment");

        // Scoping par agent : un AUTRE agent du même tenant n'a aucune capacité déclarée.
        (await harness.ExtractorCapabilitiesQueries.GetByAgentAsync(harness.TenantId, otherAgentId))
            .Should().BeNull("les capacités sont scopées (tenant, agent) — pas de fuite inter-agents.");

        // Idempotent : une re-déclaration REMPLACE la précédente (jamais cumulée).
        var updated = new ExtractorCapabilitiesDto(exposesPayments: false, regimeKeyShape: "Simple");
        await harness.BatchHandler.Handle(
            BatchWithCapabilities(harness, agentId, updated, Doc("ref-2")), CancellationToken.None);

        var reRead = await harness.ExtractorCapabilitiesQueries.GetByAgentAsync(harness.TenantId, agentId);
        reRead!.ExposesPayments.Should().BeFalse("la dernière déclaration remplace la précédente.");
        reRead.ProvidesSourceDocuments.Should().BeFalse("tous les champs reflètent la DERNIÈRE déclaration.");
        reRead.RegimeKeyShape.Should().Be("Simple");
    }

    [Fact]
    public async Task Batch_Without_Extractor_Capabilities_Persists_Nothing()
    {
        // Add-only : un agent N-1 omet les capacités (null) → aucune ligne persistée (pas d'écrasement).
        var harness = new IngestionHarness(_fixture, NewTenant());
        var agentId = Guid.NewGuid();

        await harness.BatchHandler.Handle(
            BatchWithCapabilities(harness, agentId, capabilities: null, Doc("ref-1")), CancellationToken.None);

        (await harness.ExtractorCapabilitiesQueries.GetByAgentAsync(harness.TenantId, agentId))
            .Should().BeNull("un agent qui n'en transmet pas ne crée aucune déclaration.");
    }

    private static IngestDocumentBatchCommand BatchWithCapabilities(
        IngestionHarness harness, Guid agentId, ExtractorCapabilitiesDto? capabilities, params PivotDocumentDto[] documents) =>
        new()
        {
            AgentId = agentId,
            TenantId = harness.TenantId,
            ContractVersion = "1",
            Documents = documents,
            SourceTaxRegimes = Array.Empty<SourceTaxRegimeDto>(),
            ExtractorCapabilities = capabilities,
        };

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
