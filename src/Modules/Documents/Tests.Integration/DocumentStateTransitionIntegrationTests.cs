namespace Liakont.Modules.Documents.Tests.Integration;

using System;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Documents.Domain.Entities;
using Liakont.Modules.Documents.Domain.StateMachine;
using Xunit;

/// <summary>
/// Persistance des transitions d'état (item TRK02 — INV-DOCUMENTS-009/010) sur PostgreSQL réel
/// (Testcontainers). Vérifie le read-modify-write transactionnel : chargement de l'agrégat AVEC verrou
/// (<c>GetForUpdateAsync</c>), transition de domaine, puis persistance ATOMIQUE de l'état ET de son
/// événement d'audit dans la MÊME transaction. La transition n'est visible qu'après <c>Commit</c>
/// (atomicité tout-ou-rien) ; une transition illégale ne laisse aucune trace.
/// </summary>
[Collection("DocumentsIntegration")]
public sealed class DocumentStateTransitionIntegrationTests
{
    private static readonly DateTimeOffset T0 = DocumentTestData.DetectedAt;

    // Preuves d'émission / de rejet (F06 §3 / TRK04) — JSON fictifs valides pour la persistance jsonb.
    private static readonly IssuanceSnapshots Issuance = new(
        payloadSnapshot: "{\"documentNumber\":\"F-2026-001\",\"totalGross\":1162.80}",
        paResponseSnapshot: "{\"paDocumentId\":\"PA-123\",\"taxReportId\":\"TR-9\"}",
        mappingTrace: "{\"rule\":\"S->20\",\"version\":\"2026.1\"}");

    private static readonly RejectionSnapshots Rejection = new(
        payloadSnapshot: "{\"documentNumber\":\"F-2026-001\",\"totalGross\":1162.80}",
        paResponseSnapshot: "{\"error\":\"INVALID_FORMAT\"}");

    private readonly Fixtures.DocumentsDatabaseFixture _fixture;

    public DocumentStateTransitionIntegrationTests(Fixtures.DocumentsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Transition_Persists_New_State_And_Audit_Event_Atomically()
    {
        var harness = new DocumentsHarness(_fixture);
        var id = await SeedDetectedAsync(harness);

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            var doc = await uow.GetForUpdateAsync(id);
            doc.Should().NotBeNull();
            doc!.State.Should().Be(DocumentState.Detected);

            var evt = doc.MarkReadyToSend(T0.AddMinutes(1));
            await uow.UpsertDocumentAsync(doc);
            await uow.AppendEventAsync(evt);
            await uow.CommitAsync();
        }

        var dto = await harness.Queries.GetByIdAsync(id);
        dto!.State.Should().Be(nameof(DocumentState.ReadyToSend));

        var events = await harness.Queries.GetEventsAsync(id);
        events.Should().HaveCount(2, "la genèse + la transition.");
        events[^1].EventType.Should().Be(nameof(DocumentEventType.DocumentReadyToSend));
        events[^1].OperatorIdentity.Should().BeNull("une transition du pipeline est un événement système.");
    }

    [Fact]
    public async Task Full_Nominal_Cycle_Is_Persisted_With_One_Event_Per_Transition()
    {
        var harness = new DocumentsHarness(_fixture);
        var id = await SeedDetectedAsync(harness);

        await TransitionAsync(harness, id, (doc, at) => doc.MarkReadyToSend(at), T0.AddMinutes(1));
        await TransitionAsync(harness, id, (doc, at) => doc.BeginSending(at), T0.AddMinutes(2));
        await TransitionAsync(harness, id, (doc, at) => doc.MarkIssued(Issuance, at), T0.AddMinutes(3));

        var dto = await harness.Queries.GetByIdAsync(id);
        dto!.State.Should().Be(nameof(DocumentState.Issued));

        var events = await harness.Queries.GetEventsAsync(id);
        events.Should().HaveCount(4);
        events[0].EventType.Should().Be(nameof(DocumentEventType.DocumentDetected));
        events[1].EventType.Should().Be(nameof(DocumentEventType.DocumentReadyToSend));
        events[2].EventType.Should().Be(nameof(DocumentEventType.DocumentSending));
        events[3].EventType.Should().Be(nameof(DocumentEventType.DocumentIssued));
    }

    [Fact]
    public async Task Issued_Event_Persists_The_Three_Proof_Snapshots_As_Jsonb()
    {
        var harness = new DocumentsHarness(_fixture);
        var id = await SeedDetectedAsync(harness);

        await TransitionAsync(harness, id, (doc, at) => doc.MarkReadyToSend(at), T0.AddMinutes(1));
        await TransitionAsync(harness, id, (doc, at) => doc.BeginSending(at), T0.AddMinutes(2));
        await TransitionAsync(harness, id, (doc, at) => doc.MarkIssued(Issuance, at), T0.AddMinutes(3));

        var (payload, paResponse, mappingTrace) = await ReadSnapshotsAsync(
            harness, id, nameof(DocumentEventType.DocumentIssued));

        // Les 3 snapshots (F06 §3 / TRK04) sont persistés et relus sans perte depuis les colonnes jsonb.
        payload.Should().NotBeNull().And.Contain("1162.80");
        paResponse.Should().NotBeNull().And.Contain("PA-123");
        mappingTrace.Should().NotBeNull().And.Contain("2026.1");
    }

    [Fact]
    public async Task Rejected_Event_Persists_Payload_And_Pa_Response_Without_Mapping_Trace()
    {
        var harness = new DocumentsHarness(_fixture);
        var id = await SeedDetectedAsync(harness);

        await TransitionAsync(harness, id, (doc, at) => doc.MarkReadyToSend(at), T0.AddMinutes(1));
        await TransitionAsync(harness, id, (doc, at) => doc.BeginSending(at), T0.AddMinutes(2));
        await TransitionAsync(harness, id, (doc, at) => doc.MarkRejectedByPa(Rejection, at, "Format rejeté"), T0.AddMinutes(3));

        var (payload, paResponse, mappingTrace) = await ReadSnapshotsAsync(
            harness, id, nameof(DocumentEventType.DocumentRejectedByPa));

        // La tentative ratée est archivée (payload + réponse de rejet) ; pas de trace de mapping pour un rejet.
        payload.Should().NotBeNull().And.Contain("F-2026-001");
        paResponse.Should().NotBeNull().And.Contain("INVALID_FORMAT");
        mappingTrace.Should().BeNull("un document rejeté n'a pas été émis : aucune trace de mapping (F06 §3).");
    }

    [Fact]
    public async Task Operator_Supersede_Persists_Replacement_Link_And_Operator_Identity()
    {
        var harness = new DocumentsHarness(_fixture);
        var id = await SeedDetectedAsync(harness);

        // Amène le document jusqu'à RejectedByPa, seul état d'où le remplacement est permis (F06 §4).
        await TransitionAsync(harness, id, (doc, at) => doc.MarkReadyToSend(at), T0.AddMinutes(1));
        await TransitionAsync(harness, id, (doc, at) => doc.BeginSending(at), T0.AddMinutes(2));
        await TransitionAsync(harness, id, (doc, at) => doc.MarkRejectedByPa(Rejection, at, "Format rejeté"), T0.AddMinutes(3));

        await TransitionAsync(harness, id, (doc, at) => doc.Supersede("F-2026-002", "carol@cmp", at), T0.AddMinutes(4));

        var dto = await harness.Queries.GetByIdAsync(id);
        dto!.State.Should().Be(nameof(DocumentState.Superseded));

        var events = await harness.Queries.GetEventsAsync(id);
        events[^1].EventType.Should().Be(nameof(DocumentEventType.DocumentSuperseded));
        events[^1].OperatorIdentity.Should().Be("carol@cmp");
        events[^1].Detail.Should().Contain("F-2026-002");
    }

    [Fact]
    public async Task Transition_Is_Not_Visible_Until_Commit()
    {
        var harness = new DocumentsHarness(_fixture);
        var id = await SeedDetectedAsync(harness);

        // Read-modify-write SANS commit : la sortie du bloc dispose la transaction sans la valider → rollback.
        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            var doc = await uow.GetForUpdateAsync(id);
            var evt = doc!.MarkReadyToSend(T0.AddMinutes(1));
            await uow.UpsertDocumentAsync(doc);
            await uow.AppendEventAsync(evt);

            // PAS de CommitAsync : on simule un incident avant validation.
        }

        var dto = await harness.Queries.GetByIdAsync(id);
        dto!.State.Should().Be(nameof(DocumentState.Detected), "sans commit, ni l'état ni l'événement ne sont visibles.");

        var events = await harness.Queries.GetEventsAsync(id);
        events.Should().ContainSingle("seule la genèse subsiste : la transition non validée est entièrement annulée (atomicité).");
    }

    [Fact]
    public async Task Illegal_Transition_Leaves_Document_And_Audit_Untouched()
    {
        var harness = new DocumentsHarness(_fixture);
        var id = await SeedDetectedAsync(harness);

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            var doc = await uow.GetForUpdateAsync(id);

            // Detected → Issued est interdit : l'exception survient AVANT toute écriture (et avant la capture des snapshots).
            var act = () => doc!.MarkIssued(Issuance, T0.AddMinutes(1));
            act.Should().Throw<InvalidDocumentTransitionException>();

            await uow.CommitAsync();
        }

        var dto = await harness.Queries.GetByIdAsync(id);
        dto!.State.Should().Be(nameof(DocumentState.Detected));

        var events = await harness.Queries.GetEventsAsync(id);
        events.Should().ContainSingle("une transition refusée n'écrit aucun événement.");
    }

    [Fact]
    public async Task GetForUpdate_Returns_Null_When_Document_Absent()
    {
        var harness = new DocumentsHarness(_fixture);

        await using var uow = await harness.UowFactory.BeginAsync();
        (await uow.GetForUpdateAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task Concurrent_GetForUpdate_Serializes_Transitions_And_Prevents_Lost_Update()
    {
        var harness = new DocumentsHarness(_fixture);
        var id = await SeedDetectedAsync(harness);

        await using var uow1 = await harness.UowFactory.BeginAsync();
        var doc1 = await uow1.GetForUpdateAsync(id); // T1 verrouille la ligne (FOR UPDATE).
        doc1.Should().NotBeNull();
        var evt1 = doc1!.MarkReadyToSend(T0.AddMinutes(1));
        await uow1.UpsertDocumentAsync(doc1);
        await uow1.AppendEventAsync(evt1);

        // T1 NON commité : le verrou de ligne est tenu.

        // T2 charge le MÊME document avec verrou : doit BLOQUER tant que T1 n'a pas commité.
        await using var uow2 = await harness.UowFactory.BeginAsync();
        var t2 = uow2.GetForUpdateAsync(id);

        var completedFirst = await Task.WhenAny(t2, Task.Delay(TimeSpan.FromSeconds(1)));
        completedFirst.Should().NotBeSameAs(t2, "T2 doit être bloqué par le verrou de ligne de T1, pas le contourner.");
        t2.IsCompleted.Should().BeFalse("le verrou FOR UPDATE sérialise les transitions du même document.");

        // T1 commit -> libère le verrou ; T2 doit alors voir l'ÉTAT AVANCÉ (pas de lost-update).
        await uow1.CommitAsync();

        var doc2 = await t2.WaitAsync(TimeSpan.FromSeconds(10));
        doc2!.State.Should().Be(DocumentState.ReadyToSend, "T2 part de l'état laissé par T1 commité, jamais de l'état initial.");

        var evt2 = doc2.BeginSending(T0.AddMinutes(2));
        await uow2.UpsertDocumentAsync(doc2);
        await uow2.AppendEventAsync(evt2);
        await uow2.CommitAsync();

        var dto = await harness.Queries.GetByIdAsync(id);
        dto!.State.Should().Be(nameof(DocumentState.Sending));
        (await harness.Queries.GetEventsAsync(id)).Should().HaveCount(3, "genèse + ReadyToSend (T1) + Sending (T2).");
    }

    [Fact]
    public async Task Transition_Preserves_All_Non_State_Fields()
    {
        var harness = new DocumentsHarness(_fixture);

        var seeded = DocumentTestData.NewDetected(
            documentNumber: $"TRK02-PRESERVE-{Guid.NewGuid():N}",
            sourceReference: "SRC-PRESERVE",
            supplierSiren: "987654321",
            customerName: "Acheteur SAS",
            customerIsCompanyHint: true,
            totalNet: 1000.00m,
            totalTax: 162.80m,
            totalGross: 1162.80m,
            payloadHash: "hash-preserve");

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            await uow.CreateDetectedAsync(seeded, DocumentEvent.Detected(seeded.Id, T0));
            await uow.CommitAsync();
        }

        await TransitionAsync(harness, seeded.Id, (doc, at) => doc.MarkReadyToSend(at), T0.AddMinutes(1));

        var dto = await harness.Queries.GetByIdAsync(seeded.Id);
        dto!.State.Should().Be(nameof(DocumentState.ReadyToSend));
        dto.SourceReference.Should().Be("SRC-PRESERVE");
        dto.DocumentNumber.Should().Be(seeded.DocumentNumber);
        dto.SupplierSiren.Should().Be("987654321");
        dto.CustomerName.Should().Be("Acheteur SAS");
        dto.CustomerIsCompanyHint.Should().BeTrue();
        dto.TotalNet.Should().Be(1000.00m, "un changement d'état ne doit JAMAIS altérer un montant audité (CLAUDE.md n°1/4).");
        dto.TotalTax.Should().Be(162.80m);
        dto.TotalGross.Should().Be(1162.80m);
        dto.PayloadHash.Should().Be("hash-preserve");
        dto.FirstSeenUtc.Should().Be(T0, "first_seen_utc n'est jamais écrasé par une transition.");
    }

    private static async Task<(string? Payload, string? PaResponse, string? MappingTrace)> ReadSnapshotsAsync(
        DocumentsHarness harness,
        Guid documentId,
        string eventType)
    {
        using var conn = await harness.ConnectionFactory.OpenAsync();
        var row = await conn.QuerySingleAsync(
            """
            SELECT payload_snapshot::text     AS payload,
                   pa_response_snapshot::text AS paresponse,
                   mapping_trace::text        AS mappingtrace
            FROM documents.document_events
            WHERE document_id = @d AND event_type = @t
            """,
            new { d = documentId, t = eventType });

        return ((string?)row.payload, (string?)row.paresponse, (string?)row.mappingtrace);
    }

    private static async Task<Guid> SeedDetectedAsync(DocumentsHarness harness)
    {
        var document = DocumentTestData.NewDetected(documentNumber: $"TRK02-{Guid.NewGuid():N}");
        await using var uow = await harness.UowFactory.BeginAsync();
        await uow.CreateDetectedAsync(document, DocumentEvent.Detected(document.Id, T0));
        await uow.CommitAsync();
        return document.Id;
    }

    private static async Task TransitionAsync(
        DocumentsHarness harness,
        Guid id,
        Func<Document, DateTimeOffset, DocumentEvent> transition,
        DateTimeOffset occurredAtUtc)
    {
        await using var uow = await harness.UowFactory.BeginAsync();
        var doc = await uow.GetForUpdateAsync(id);
        doc.Should().NotBeNull();

        var evt = transition(doc!, occurredAtUtc);
        await uow.UpsertDocumentAsync(doc!);
        await uow.AppendEventAsync(evt);
        await uow.CommitAsync();
    }
}
