namespace Liakont.Modules.Documents.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.Documents.Domain.Entities;
using Xunit;

/// <summary>
/// La factory <see cref="DocumentEvent.PaTransmissionJournaled"/> (item FX06) produit un fait d'audit SYSTÈME
/// de journalisation d'envoi PA : type dédié, colonnes de transmission renseignées, réponse PA portée par le
/// snapshot existant, et champs obligatoires validés (aucun envoi tracé à blanc).
/// </summary>
public sealed class DocumentEventPaTransmissionTests
{
    private static readonly DateTimeOffset RequestUtc = new(2026, 6, 16, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ResponseUtc = new(2026, 6, 16, 3, 0, 2, TimeSpan.Zero);

    [Fact]
    public void Builds_A_System_Pa_Transmission_Journal_Event()
    {
        var documentId = Guid.NewGuid();

        var ev = DocumentEvent.PaTransmissionJournaled(
            documentId,
            ResponseUtc,
            paAccount: "  compte-pa  ",
            paPluginId: "generique",
            paRequestUtc: RequestUtc,
            paResponseUtc: ResponseUtc,
            transmittedArtifactHash: "sha256-artefact",
            idempotencyKey: "idem-001",
            paResponseSnapshot: "{\"status\":\"accepted\"}",
            detail: "Factur-X transmis (FX06).");

        ev.EventType.Should().Be(DocumentEventType.DocumentPaTransmissionJournaled);
        ev.DocumentId.Should().Be(documentId);
        ev.OperatorIdentity.Should().BeNull("la transmission est un événement système, pas une action opérateur");
        ev.PaAccount.Should().Be("compte-pa", "les valeurs sont élaguées");
        ev.PaPluginId.Should().Be("generique");
        ev.PaRequestUtc.Should().Be(RequestUtc);
        ev.PaResponseUtc.Should().Be(ResponseUtc);
        ev.TransmittedArtifactHash.Should().Be("sha256-artefact");
        ev.IdempotencyKey.Should().Be("idem-001");
        ev.PaResponseSnapshot.Should().Contain("accepted", "la réponse PA réutilise le snapshot existant, jamais une colonne doublon");
        ev.PayloadSnapshot.Should().BeNull();
        ev.MappingTrace.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_A_Blank_Idempotency_Key(string idempotencyKey)
    {
        var act = () => DocumentEvent.PaTransmissionJournaled(
            Guid.NewGuid(), ResponseUtc, "compte", "generique", RequestUtc, ResponseUtc,
            "hash", idempotencyKey, "{}", "détail");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rejects_A_Blank_Pa_Account()
    {
        var act = () => DocumentEvent.PaTransmissionJournaled(
            Guid.NewGuid(), ResponseUtc, "  ", "generique", RequestUtc, ResponseUtc,
            "hash", "idem-001", "{}", "détail");

        act.Should().Throw<ArgumentException>();
    }
}
