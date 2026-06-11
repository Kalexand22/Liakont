namespace Liakont.Modules.Documents.Tests.Integration;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Documents.Domain.Entities;
using Liakont.Modules.Documents.Infrastructure.Lifecycle;
using Xunit;

/// <summary>
/// Le port <see cref="DocumentLifecycle"/> (PIP01a) sur PostgreSQL réel (Testcontainers) : prouve, via la
/// vraie unité de travail (verrou FOR UPDATE → transition → upsert état + événement d'audit append-only →
/// commit), que l'état ET la piste d'audit atterrissent ATOMIQUEMENT (CLAUDE.md n°4), et que
/// <c>MarkReadyToSendAsync</c> persiste réellement <c>mapping_version</c> de bout en bout. Clés uniques par
/// test → robuste à la fixture partagée.
/// </summary>
[Collection("DocumentsIntegration")]
public sealed class DocumentLifecycleIntegrationTests
{
    private static readonly DateTimeOffset T0 = DocumentTestData.DetectedAt;

    private readonly Fixtures.DocumentsDatabaseFixture _fixture;

    public DocumentLifecycleIntegrationTests(Fixtures.DocumentsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BlockAsync_Persists_State_And_Audit_Event_Atomically()
    {
        var harness = new DocumentsHarness(_fixture);
        var id = await SeedDetectedAsync(harness);
        var lifecycle = new DocumentLifecycle(harness.UowFactory, harness.Queries);

        await lifecycle.BlockAsync(id, "Table TVA non validée — validation expert-comptable requise");

        var dto = await harness.Queries.GetByIdAsync(id);
        dto!.State.Should().Be(nameof(DocumentState.Blocked));

        var events = await harness.Queries.GetEventsAsync(id);
        events.Should().HaveCount(2, "la genèse + le blocage.");
        events[^1].EventType.Should().Be(nameof(DocumentEventType.DocumentBlocked));
        events[^1].Detail.Should().Contain("Table TVA non validée");
    }

    [Fact]
    public async Task MarkReadyToSendAsync_Persists_State_And_Mapping_Version_End_To_End()
    {
        var harness = new DocumentsHarness(_fixture);
        var id = await SeedDetectedAsync(harness);
        var lifecycle = new DocumentLifecycle(harness.UowFactory, harness.Queries);

        await lifecycle.MarkReadyToSendAsync(id, "2026.1");

        var ready = await harness.Queries.GetByIdAsync(id);
        ready!.State.Should().Be(nameof(DocumentState.ReadyToSend));
        ready.MappingVersion.Should().Be("2026.1", "la version de mapping doit être persistée de bout en bout (F03).");

        // On poursuit jusqu'à Sending : (a) prouve que mapping_version SURVIT à la transition suivante, et
        // (b) ne laisse aucun document ReadyToSend résiduel — un comptage par état sur la fixture partagée
        // (GetByState) suppose un total exact (leçon « pollution de fixture partagée »).
        await lifecycle.BeginSendingAsync(id);

        var sending = await harness.Queries.GetByIdAsync(id);
        sending!.State.Should().Be(nameof(DocumentState.Sending));
        sending.MappingVersion.Should().Be("2026.1");
    }

    [Fact]
    public async Task Lifecycle_On_Unknown_Document_Throws()
    {
        var harness = new DocumentsHarness(_fixture);
        var lifecycle = new DocumentLifecycle(harness.UowFactory, harness.Queries);

        var act = async () => await lifecycle.BlockAsync(Guid.NewGuid(), "motif");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static async Task<Guid> SeedDetectedAsync(DocumentsHarness harness)
    {
        var document = DocumentTestData.NewDetected(
            documentNumber: $"PIP01A-LC-{Guid.NewGuid():N}",
            sourceReference: $"SRC-{Guid.NewGuid():N}",
            payloadHash: $"hash-{Guid.NewGuid():N}");

        await using var uow = await harness.UowFactory.BeginAsync();
        await uow.CreateDetectedAsync(document, DocumentEvent.Detected(document.Id, T0));
        await uow.CommitAsync();
        return document.Id;
    }
}
