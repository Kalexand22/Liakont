namespace Liakont.Modules.Documents.Tests.Integration;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Documents.Domain.Entities;
using Liakont.Modules.Documents.Infrastructure;
using Liakont.Modules.Documents.Tests.Integration.Doubles;
using Liakont.Modules.Ingestion.Contracts.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Events;
using Xunit;

/// <summary>
/// Consommation de l'altération source APRÈS émission (item TRK03, F06 §3) sur PostgreSQL réel
/// (Testcontainers). Quand une <c>source_reference</c> déjà émise est re-poussée avec une empreinte
/// différente, un fait d'audit append-only est inscrit SUR le document émis — JAMAIS de réémission ni de
/// mise à jour du document émis. Idempotent (rejeu d'outbox at-least-once). Si aucun document n'est émis
/// pour la référence, rien n'est inscrit.
/// </summary>
[Collection("DocumentsIntegration")]
public sealed class SourceAlterationConsumerIntegrationTests
{
    private static readonly DateTimeOffset DetectedAt = new(2026, 5, 14, 10, 0, 0, TimeSpan.Zero);

    private readonly Fixtures.DocumentsDatabaseFixture _fixture;

    public SourceAlterationConsumerIntegrationTests(Fixtures.DocumentsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Alteration_After_Issue_Records_An_Append_Only_Audit_Fact_On_The_Issued_Document()
    {
        var harness = new DocumentsHarness(_fixture);
        var sourceReference = Unique("SRC");
        var issued = DocumentTestData.Reconstituted(
            DocumentState.Issued, sourceReference: sourceReference, documentNumber: Unique("F"), payloadHash: "hash-emise");
        await SeedAsync(harness, issued);

        await ConsumeAsync(EventFor(sourceReference, previousHash: "hash-emise", newHash: "hash-altere"));

        var events = await harness.Queries.GetEventsAsync(issued.Id);
        events.Should().ContainSingle();
        var fact = events[0];
        fact.EventType.Should().Be(nameof(DocumentEventType.DocumentSourceAlteredAfterIssue));
        fact.OperatorIdentity.Should().BeNull("événement système.");
        fact.Detail.Should().Contain(sourceReference).And.Contain("n'est NI modifié NI réémis");

        // Le document émis est INCHANGÉ : toujours Issued, jamais réémis.
        var doc = await harness.Queries.GetByIdAsync(issued.Id);
        doc!.State.Should().Be(nameof(DocumentState.Issued));
    }

    [Fact]
    public async Task Consuming_The_Same_Event_Twice_Records_The_Alteration_Only_Once()
    {
        var harness = new DocumentsHarness(_fixture);
        var sourceReference = Unique("SRC");
        var issued = DocumentTestData.Reconstituted(
            DocumentState.Issued, sourceReference: sourceReference, documentNumber: Unique("F"), payloadHash: "hash-emise");
        await SeedAsync(harness, issued);

        var evt = EventFor(sourceReference, previousHash: "hash-emise", newHash: "hash-altere");
        await ConsumeAsync(evt);
        await ConsumeAsync(evt); // rejeu (livraison at-least-once)

        var events = await harness.Queries.GetEventsAsync(issued.Id);
        events.Count(e => e.EventType == nameof(DocumentEventType.DocumentSourceAlteredAfterIssue))
            .Should().Be(1, "la consommation est idempotente (clé primaire = identifiant de l'événement).");
    }

    [Fact]
    public async Task Alteration_Of_A_Source_Without_An_Issued_Document_Records_Nothing()
    {
        var harness = new DocumentsHarness(_fixture);
        var sourceReference = Unique("SRC");

        // Document pour la référence mais NON émis (Detected) : il n'y a pas d'émis à signaler.
        var detected = DocumentTestData.Reconstituted(
            DocumentState.Detected, sourceReference: sourceReference, documentNumber: Unique("F"), payloadHash: "hash-1");
        await SeedAsync(harness, detected);

        await ConsumeAsync(EventFor(sourceReference, previousHash: "hash-1", newHash: "hash-2"));

        var events = await harness.Queries.GetEventsAsync(detected.Id);
        events.Should().BeEmpty("aucun document émis pour la référence : rien à inscrire.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────

    private static IntegrationEvent<SourceAlterationDetectedV1> EventFor(string sourceReference, string previousHash, string newHash)
    {
        return new IntegrationEvent<SourceAlterationDetectedV1>
        {
            EventId = Guid.NewGuid(),
            EventType = IngestionEventTypes.SourceAlterationDetected,
            OccurredAt = DetectedAt,
            CorrelationId = Guid.NewGuid(),
            ModuleSource = "ingestion",
            Version = 1,
            Payload = new SourceAlterationDetectedV1
            {
                TenantId = "tenant-test",
                SourceReference = sourceReference,
                PreviousPayloadHash = previousHash,
                NewPayloadHash = newHash,
                DocumentId = Guid.NewGuid(),
                DetectedAtUtc = DetectedAt,
            },
        };
    }

    private static async Task SeedAsync(DocumentsHarness harness, Document document)
    {
        await using var uow = await harness.UowFactory.BeginAsync();
        await uow.UpsertDocumentAsync(document);
        await uow.CommitAsync();
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private Task ConsumeAsync(IntegrationEvent<SourceAlterationDetectedV1> integrationEvent)
    {
        var consumer = new SourceAlterationDetectedConsumer(
            new SingleDatabaseTenantConnectionFactory(_fixture.ConnectionString),
            NullLogger<SourceAlterationDetectedConsumer>.Instance);
        return consumer.HandleAsync(integrationEvent);
    }
}
