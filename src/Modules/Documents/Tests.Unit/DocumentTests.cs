namespace Liakont.Modules.Documents.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.Documents.Domain.Entities;
using Xunit;

/// <summary>
/// Invariants de l'agrégat <see cref="Document"/> et de l'événement de genèse — INV-DOCUMENTS-001/004/005.
/// </summary>
public sealed class DocumentTests
{
    private static readonly DateTimeOffset DetectedAt = new(2026, 5, 14, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void CreateDetected_Sets_State_Detected_And_Timestamps()
    {
        var doc = CreateValid();

        doc.State.Should().Be(DocumentState.Detected);
        doc.FirstSeenUtc.Should().Be(DetectedAt);
        doc.LastUpdateUtc.Should().Be(DetectedAt);
        doc.PaDocumentId.Should().BeNull("le document n'est pas encore transmis à une PA.");
        doc.MappingVersion.Should().BeNull("le mapping est appliqué en aval (pipeline).");
    }

    [Fact]
    public void CreateDetected_Preserves_Decimal_Amounts_Exactly()
    {
        var doc = Build(totalNet: 1000.00m, totalTax: 162.80m, totalGross: 1162.80m);

        doc.TotalNet.Should().Be(1000.00m);
        doc.TotalTax.Should().Be(162.80m);
        doc.TotalGross.Should().Be(1162.80m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateDetected_Requires_SourceReference(string blank)
    {
        var act = () => Build(sourceReference: blank);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateDetected_Requires_DocumentNumber(string blank)
    {
        var act = () => Build(documentNumber: blank);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateDetected_Requires_DocumentType(string blank)
    {
        var act = () => Build(documentType: blank);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateDetected_Requires_PayloadHash(string blank)
    {
        var act = () => Build(payloadHash: blank);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateDetected_Trims_Mandatory_Strings()
    {
        var doc = Build(documentNumber: "  F-1  ", sourceReference: " SRC ", payloadHash: " hash ", documentType: " FAC ");

        doc.DocumentNumber.Should().Be("F-1");
        doc.SourceReference.Should().Be("SRC");
        doc.PayloadHash.Should().Be("hash");
        doc.DocumentType.Should().Be("FAC");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateDetected_Blank_Siren_And_Customer_Become_Null(string? blank)
    {
        var doc = Build(supplierSiren: blank, customerName: blank);

        doc.SupplierSiren.Should().BeNull();
        doc.CustomerName.Should().BeNull("un champ source absent reste null, jamais un défaut implicite (blueprint §8).");
    }

    [Fact]
    public void DocumentEvent_Detected_Is_System_Genesis()
    {
        var documentId = Guid.NewGuid();

        var evt = DocumentEvent.Detected(documentId, DetectedAt);

        evt.DocumentId.Should().Be(documentId);
        evt.TimestampUtc.Should().Be(DetectedAt);
        evt.EventType.Should().Be(DocumentEventType.DocumentDetected);
        evt.OperatorIdentity.Should().BeNull("la genèse par l'ingestion est un événement système, sans opérateur.");
        evt.Detail.Should().NotBeNullOrWhiteSpace();
        evt.PayloadSnapshot.Should().BeNull();
        evt.PaResponseSnapshot.Should().BeNull();
        evt.MappingTrace.Should().BeNull();
    }

    private static Document CreateValid() => Build();

    private static Document Build(
        string sourceReference = "SRC-1",
        string documentNumber = "F-2026-001",
        string documentType = "FAC",
        string? supplierSiren = "123456789",
        string? customerName = "Client SARL",
        bool customerIsCompanyHint = true,
        decimal totalNet = 100.00m,
        decimal totalTax = 20.00m,
        decimal totalGross = 120.00m,
        string payloadHash = "deadbeef")
    {
        return Document.CreateDetected(
            id: Guid.NewGuid(),
            sourceReference: sourceReference,
            documentNumber: documentNumber,
            documentType: documentType,
            issueDate: new DateOnly(2026, 5, 14),
            supplierSiren: supplierSiren,
            customerName: customerName,
            customerIsCompanyHint: customerIsCompanyHint,
            totalNet: totalNet,
            totalTax: totalTax,
            totalGross: totalGross,
            payloadHash: payloadHash,
            detectedAtUtc: DetectedAt);
    }
}
