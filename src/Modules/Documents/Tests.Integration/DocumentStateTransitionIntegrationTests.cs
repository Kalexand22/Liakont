namespace Liakont.Modules.Documents.Tests.Integration;

using System;
using System.Threading.Tasks;
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
        await TransitionAsync(harness, id, (doc, at) => doc.MarkIssued(at), T0.AddMinutes(3));

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
    public async Task Operator_Supersede_Persists_Replacement_Link_And_Operator_Identity()
    {
        var harness = new DocumentsHarness(_fixture);
        var id = await SeedDetectedAsync(harness);

        // Amène le document jusqu'à RejectedByPa, seul état d'où le remplacement est permis (F06 §4).
        await TransitionAsync(harness, id, (doc, at) => doc.MarkReadyToSend(at), T0.AddMinutes(1));
        await TransitionAsync(harness, id, (doc, at) => doc.BeginSending(at), T0.AddMinutes(2));
        await TransitionAsync(harness, id, (doc, at) => doc.MarkRejectedByPa(at, "Format rejeté"), T0.AddMinutes(3));

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

            // Detected → Issued est interdit : l'exception survient AVANT toute écriture.
            var act = () => doc!.MarkIssued(T0.AddMinutes(1));
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
