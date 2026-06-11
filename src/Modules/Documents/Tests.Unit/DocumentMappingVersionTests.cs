namespace Liakont.Modules.Documents.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.Documents.Domain.Entities;
using Xunit;

/// <summary>
/// <c>MarkReadyToSendWithMapping(occurredAt, mappingVersion, detail)</c> (PIP01a) : consigne la version de
/// table de mapping TVA appliquée au passage ReadyToSend (traçabilité F03/F06 §3), après la garde de légalité.
/// </summary>
public sealed class DocumentMappingVersionTests
{
    private static readonly DateTimeOffset At = new(2026, 5, 14, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MarkReadyToSendWithMapping_Records_The_Version()
    {
        var document = Detected();

        var documentEvent = document.MarkReadyToSendWithMapping(At.AddMinutes(1), "2026.1");

        document.State.Should().Be(DocumentState.ReadyToSend);
        document.MappingVersion.Should().Be("2026.1");
        documentEvent.EventType.Should().Be(DocumentEventType.DocumentReadyToSend);
        documentEvent.OperatorIdentity.Should().BeNull("un déblocage système (pipeline) n'a pas d'opérateur.");
    }

    [Fact]
    public void MarkReadyToSendWithMapping_Records_The_Operator_When_Provided()
    {
        // FIX02 : un déblocage par re-vérification opérateur attribue l'événement ReadyToSend à l'opérateur
        // (geste tracé, pas un déblocage système anonyme).
        var document = Detected();

        var documentEvent = document.MarkReadyToSendWithMapping(At.AddMinutes(1), "2026.1", detail: "Débloqué par re-vérification.", operatorIdentity: "alice@cmp");

        document.State.Should().Be(DocumentState.ReadyToSend);
        documentEvent.EventType.Should().Be(DocumentEventType.DocumentReadyToSend);
        documentEvent.OperatorIdentity.Should().Be("alice@cmp");
    }

    [Fact]
    public void MarkReadyToSendWithMapping_Trims_The_Version()
    {
        var document = Detected();

        document.MarkReadyToSendWithMapping(At.AddMinutes(1), "  2026.2  ");

        document.MappingVersion.Should().Be("2026.2");
    }

    [Fact]
    public void MarkReadyToSendWithMapping_With_Blank_Version_Throws_And_Leaves_State_Untouched()
    {
        var document = Detected();

        var act = () => document.MarkReadyToSendWithMapping(At.AddMinutes(1), "   ");

        act.Should().Throw<ArgumentException>();
        document.State.Should().Be(DocumentState.Detected);
        document.MappingVersion.Should().BeNull("une version invalide ne doit laisser aucune trace.");
    }

    private static Document Detected() => Document.CreateDetected(
        Guid.NewGuid(),
        "SRC-1",
        "F-2026-001",
        "FAC",
        new DateOnly(2026, 5, 14),
        supplierSiren: "123456789",
        customerName: "Client SARL",
        customerIsCompanyHint: true,
        totalNet: 100.00m,
        totalTax: 20.00m,
        totalGross: 120.00m,
        payloadHash: "hash-1",
        detectedAtUtc: At);
}
