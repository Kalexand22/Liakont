namespace Liakont.Modules.Documents.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.Documents.Domain.Entities;
using Xunit;

/// <summary>
/// Surcharge <c>MarkReadyToSend(occurredAt, mappingVersion, detail)</c> (PIP01a) : consigne la version de
/// table de mapping TVA appliquée au passage ReadyToSend (traçabilité F03/F06 §3).
/// </summary>
public sealed class DocumentMappingVersionTests
{
    private static readonly DateTimeOffset At = new(2026, 5, 14, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MarkReadyToSend_With_Mapping_Version_Records_It()
    {
        var document = Detected();

        var documentEvent = document.MarkReadyToSend(At.AddMinutes(1), mappingVersion: "2026.1");

        document.State.Should().Be(DocumentState.ReadyToSend);
        document.MappingVersion.Should().Be("2026.1");
        documentEvent.EventType.Should().Be(DocumentEventType.DocumentReadyToSend);
    }

    [Fact]
    public void MarkReadyToSend_Trims_The_Mapping_Version()
    {
        var document = Detected();

        document.MarkReadyToSend(At.AddMinutes(1), mappingVersion: "  2026.2  ");

        document.MappingVersion.Should().Be("2026.2");
    }

    [Fact]
    public void MarkReadyToSend_With_Blank_Mapping_Version_Throws_And_Leaves_State_Untouched()
    {
        var document = Detected();

        var act = () => document.MarkReadyToSend(At.AddMinutes(1), mappingVersion: "   ");

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
