namespace Liakont.Modules.Documents.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Documents.Domain.Entities;
using Liakont.Modules.Documents.Infrastructure;
using Liakont.Modules.Ingestion.Contracts;
using Xunit;

/// <summary>
/// Projection pivot -> Document en état Detected (INV-DOCUMENTS-002/003) : report fidèle des montants
/// source (decimal), du type BRUT et des indices, sans calcul ni classification (frontière module-rules §2).
/// </summary>
public sealed class DetectedDocumentMapperTests
{
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly DateTimeOffset ReceivedAt = new(2026, 5, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Maps_Core_Fields_From_Pivot_And_Intake()
    {
        var doc = DetectedDocumentMapper.ToDetectedDocument(BuildIntake());

        doc.Id.Should().Be(DocumentId);
        doc.SourceReference.Should().Be("SRC-1");
        doc.PayloadHash.Should().Be("hash-1");
        doc.DocumentNumber.Should().Be("F-2026-001");
        doc.DocumentType.Should().Be("FAC", "le type BRUT de la source est reporté tel quel (classification déléguée à Validation).");
        doc.IssueDate.Should().Be(new DateOnly(2026, 5, 14));
        doc.SupplierSiren.Should().Be("123456789");
        doc.State.Should().Be(DocumentState.Detected);
        doc.FirstSeenUtc.Should().Be(ReceivedAt);
    }

    [Fact]
    public void Preserves_Tricky_Decimal_Amounts()
    {
        var doc = DetectedDocumentMapper.ToDetectedDocument(
            BuildIntake(totals: new PivotTotalsDto(1000.00m, 162.80m, 1162.80m)));

        doc.TotalNet.Should().Be(1000.00m);
        doc.TotalTax.Should().Be(162.80m);
        doc.TotalGross.Should().Be(1162.80m);
    }

    [Fact]
    public void Maps_Company_Hint_When_Customer_Is_Company()
    {
        var doc = DetectedDocumentMapper.ToDetectedDocument(
            BuildIntake(customer: new PivotPartyDto("Client SARL", isCompanyHint: true)));

        doc.CustomerName.Should().Be("Client SARL");
        doc.CustomerIsCompanyHint.Should().BeTrue();
    }

    [Fact]
    public void Maps_Null_Customer_To_Null_Name_And_False_Hint()
    {
        var doc = DetectedDocumentMapper.ToDetectedDocument(BuildIntake(customer: null));

        doc.CustomerName.Should().BeNull("B2C sans tiers identifié : pas de destinataire.");
        doc.CustomerIsCompanyHint.Should().BeFalse();
    }

    [Fact]
    public void Maps_Null_Supplier_Siren()
    {
        var doc = DetectedDocumentMapper.ToDetectedDocument(
            BuildIntake(supplier: new PivotPartyDto("Ma SVV", siren: null)));

        doc.SupplierSiren.Should().BeNull();
    }

    private static DetectedDocumentIntake BuildIntake(
        PivotPartyDto? supplier = null,
        PivotTotalsDto? totals = null,
        PivotPartyDto? customer = null)
    {
        var pivot = new PivotDocumentDto(
            sourceDocumentKind: "FAC",
            number: "F-2026-001",
            issueDate: new DateTime(2026, 5, 14),
            sourceReference: "SRC-1",
            supplier: supplier ?? new PivotPartyDto("Ma SVV", siren: "123456789"),
            totals: totals ?? new PivotTotalsDto(100.00m, 20.00m, 120.00m),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: customer);

        return new DetectedDocumentIntake
        {
            DocumentId = DocumentId,
            TenantId = "acme",
            SourceReference = "SRC-1",
            PayloadHash = "hash-1",
            Document = pivot,
            ReceivedAtUtc = ReceivedAt,
        };
    }
}
