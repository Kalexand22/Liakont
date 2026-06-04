namespace Liakont.Modules.Documents.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.Documents.Domain.Entities;
using Xunit;

/// <summary>
/// Fait d'audit d'altération source après émission (item TRK03, F06 §3) : événement SYSTÈME, sans
/// snapshot, identifiant porté par l'appelant (idempotence de la consommation d'outbox).
/// </summary>
public sealed class DocumentEventSourceAlteredTests
{
    private static readonly DateTimeOffset DetectedAt = new(2026, 5, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SourceAlteredAfterIssue_Is_A_System_Audit_Fact_Without_Snapshot()
    {
        var eventId = Guid.NewGuid();
        var issuedDocumentId = Guid.NewGuid();

        var evt = DocumentEvent.SourceAlteredAfterIssue(eventId, issuedDocumentId, DetectedAt, "  altération détectée  ");

        evt.Id.Should().Be(eventId, "l'identifiant est celui de l'événement d'intégration (idempotence).");
        evt.DocumentId.Should().Be(issuedDocumentId, "le fait est inscrit SUR le document émis.");
        evt.TimestampUtc.Should().Be(DetectedAt);
        evt.EventType.Should().Be(DocumentEventType.DocumentSourceAlteredAfterIssue);
        evt.Detail.Should().Be("altération détectée", "le détail est nettoyé de ses espaces de bord.");
        evt.OperatorIdentity.Should().BeNull("événement système (aucun opérateur).");
        evt.PayloadSnapshot.Should().BeNull();
        evt.PaResponseSnapshot.Should().BeNull();
        evt.MappingTrace.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SourceAlteredAfterIssue_Requires_A_Detail(string blank)
    {
        var act = () => DocumentEvent.SourceAlteredAfterIssue(Guid.NewGuid(), Guid.NewGuid(), DetectedAt, blank);

        act.Should().Throw<ArgumentException>();
    }
}
