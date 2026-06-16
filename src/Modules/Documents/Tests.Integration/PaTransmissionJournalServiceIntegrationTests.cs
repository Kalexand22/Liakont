namespace Liakont.Modules.Documents.Tests.Integration;

using System;
using FluentAssertions;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Domain.Entities;
using Liakont.Modules.Documents.Infrastructure.Lifecycle;
using Liakont.Modules.Documents.Infrastructure.Queries;
using Xunit;

/// <summary>
/// Le port d'ÉCRITURE de journalisation d'envoi PA (<see cref="IPaTransmissionJournal"/>, item FX07) consigne
/// réellement un fait d'audit append-only sur <c>documents.document_events</c> et le rend recherchable par sa
/// clé d'idempotence (read <see cref="IPaTransmissionJournalQueries"/>). Complète
/// <see cref="PaTransmissionJournalIntegrationTests"/> (round-trip des colonnes via la factory de domaine) en
/// vérifiant le chemin Contracts → Infrastructure → base RÉELLE que le pipeline emprunte.
/// </summary>
[Collection("DocumentsIntegration")]
public sealed class PaTransmissionJournalServiceIntegrationTests
{
    private static readonly DateTimeOffset RequestUtc = new(2026, 6, 16, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ResponseUtc = new(2026, 6, 16, 8, 0, 3, TimeSpan.Zero);

    private readonly Fixtures.DocumentsDatabaseFixture _fixture;

    public PaTransmissionJournalServiceIntegrationTests(Fixtures.DocumentsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task JournalAsync_Appends_A_Searchable_Pa_Transmission_Event()
    {
        var harness = new DocumentsHarness(_fixture);
        var documentId = await SeedDocumentAsync(harness);
        var idempotencyKey = $"FAC-{Guid.NewGuid():N}";

        var journal = new PaTransmissionJournal(harness.UowFactory);
        await journal.JournalAsync(new PaTransmissionJournalEntry
        {
            DocumentId = documentId,
            PaAccount = "compte-generique",
            PaPluginId = "generique",
            PaRequestUtc = RequestUtc,
            PaResponseUtc = ResponseUtc,
            TransmittedArtifactHash = "sha256:abcdef",
            IdempotencyKey = idempotencyKey,
            PaResponseSnapshot = "{\"status\":\"accepted\"}",
            Detail = "Factur-X transmis (FX07).",
        });

        var queries = new PostgresDocumentQueries(harness.ConnectionFactory);
        var found = await queries.FindByIdempotencyKeyAsync(idempotencyKey);

        found.Should().NotBeNull("le port d'écriture pose un fait recherchable par clé d'idempotence (F16 §7).");
        found!.DocumentId.Should().Be(documentId);
        found.IdempotencyKey.Should().Be(idempotencyKey);
        found.PaAccount.Should().Be("compte-generique");
        found.PaPluginId.Should().Be("generique");
        found.TransmittedArtifactHash.Should().Be("sha256:abcdef");
        found.PaRequestUtc.Should().Be(RequestUtc);
        found.PaResponseUtc.Should().Be(ResponseUtc);
    }

    [Fact]
    public async Task JournalAsync_Does_Not_Transition_Document_State()
    {
        var harness = new DocumentsHarness(_fixture);
        var documentId = await SeedDocumentAsync(harness);

        var journal = new PaTransmissionJournal(harness.UowFactory);
        await journal.JournalAsync(new PaTransmissionJournalEntry
        {
            DocumentId = documentId,
            PaAccount = "compte-generique",
            PaPluginId = "generique",
            PaRequestUtc = RequestUtc,
            PaResponseUtc = ResponseUtc,
            TransmittedArtifactHash = "sha256:fedcba",
            IdempotencyKey = $"FAC-{Guid.NewGuid():N}",
            PaResponseSnapshot = "{\"status\":\"accepted\"}",
            Detail = "Factur-X transmis (FX07).",
        });

        // La journalisation est un PUR ajout d'audit : le document reste dans son état (aucune transition).
        var document = await harness.Queries.GetByIdAsync(documentId);
        document.Should().NotBeNull();
        document!.State.Should().Be("Detected");
    }

    private static async Task<Guid> SeedDocumentAsync(DocumentsHarness harness)
    {
        var document = DocumentTestData.NewDetected(documentNumber: $"AO-{Guid.NewGuid():N}");
        await using var uow = await harness.UowFactory.BeginAsync();
        await uow.CreateDetectedAsync(document, DocumentEvent.Detected(document.Id, DocumentTestData.DetectedAt));
        await uow.CommitAsync();
        return document.Id;
    }
}
