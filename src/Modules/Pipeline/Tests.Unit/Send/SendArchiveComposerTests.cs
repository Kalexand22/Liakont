namespace Liakont.Modules.Pipeline.Tests.Unit.Send;

using System;
using System.Linq;
using FluentAssertions;
using Liakont.Modules.Pipeline.Infrastructure.Send;
using Xunit;

/// <summary>
/// Composition de la requête d'archivage WORM (TRK05) à partir du pivot : montants en <see cref="decimal"/>
/// repris à l'identique (aucun float), ventilation TVA agrégée par taux, motifs d'absence explicites.
/// </summary>
public sealed class SendArchiveComposerTests
{
    [Fact]
    public void Compose_Maps_Lines_And_Totals_In_Decimal()
    {
        var id = Guid.NewGuid();
        var document = SendTestData.Document(id, "Sending");
        var pivot = SendTestData.SingleLinePivot();

        var request = SendArchiveComposer.Compose(document, pivot, "{\"payload\":1}", "{\"pa\":1}", "{\"mappingVersion\":\"cmp-v1\"}");

        request.DocumentId.Should().Be(id);
        request.DocumentNumber.Should().Be(document.DocumentNumber);
        request.IssueDate.Should().Be(document.IssueDate);
        request.Readable.TotalNet.Should().Be(120.00m);
        request.Readable.TotalTax.Should().Be(24.00m);
        request.Readable.TotalGross.Should().Be(144.00m);
        request.Readable.Lines.Should().ContainSingle();
        request.Readable.Lines[0].NetAmount.Should().Be(120.00m);
        request.Readable.Lines[0].VatRateLabel.Should().Be("20 %");
        request.Readable.SellerSiren.Should().Be("404833048");
        request.Readable.BuyerName.Should().Be("Client SARL");
    }

    [Fact]
    public void Compose_Aggregates_VatBreakdown_By_Rate()
    {
        var document = SendTestData.Document(Guid.NewGuid(), "Sending", number: "F-2026-0042");
        var pivot = SendTestData.MultiRatePivot();

        var request = SendArchiveComposer.Compose(document, pivot, "{}", "{}", "{}");

        // Deux lignes à 20 % (100 + 50 net ; 20 + 10 TVA) regroupées ; une ligne à 10 % (200 net ; 20 TVA).
        request.Readable.VatBreakdown.Should().HaveCount(2);
        var twenty = request.Readable.VatBreakdown.Single(v => v.VatRateLabel == "20 %");
        twenty.TaxableBase.Should().Be(150.00m);
        twenty.TaxAmount.Should().Be(30.00m);
        var ten = request.Readable.VatBreakdown.Single(v => v.VatRateLabel == "10 %");
        ten.TaxableBase.Should().Be(200.00m);
        ten.TaxAmount.Should().Be(20.00m);
    }

    [Fact]
    public void Compose_Sets_Explicit_Absence_Reasons_For_PaInvoice_And_SourceDocument()
    {
        var document = SendTestData.Document(Guid.NewGuid(), "Sending");
        var pivot = SendTestData.SingleLinePivot();

        var request = SendArchiveComposer.Compose(document, pivot, "{}", "{}", "{}");

        request.PaInvoice.Should().BeNull();
        request.PaInvoiceAbsenceReason.Should().NotBeNullOrWhiteSpace();
        request.SourceDocument.Should().BeNull();
        request.SourceDocumentAbsenceReason.Should().NotBeNullOrWhiteSpace();
    }
}
