namespace Liakont.Agent.Adapters.DemoErpB.Tests;

using System;
using FluentAssertions;
using Liakont.Agent.Adapters.DemoErpB;
using Liakont.Agent.Adapters.DemoErpB.Source;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Core.Extraction;
using Xunit;

/// <summary>
/// Transformation facture DemoErpB (<c>float</c> legacy) → pivot EN 16931 : conversion float→decimal
/// arrondi half-up au centime (ADR-0004 D3-7), régimes BRUTS, <c>CategoryCode</c>/<c>VatexCode</c> nuls
/// (R3), type de pièce brut « I »/« C », lien d'avoir, et blocage sur origine d'avoir manquante.
/// </summary>
public class DemoErpBRowMapperTests
{
    [Fact]
    public void Maps_invoice_with_float_amounts_converted_to_decimal_and_raw_regime()
    {
        PivotDocumentDto doc = DemoErpBRowMapper.MapDocument(SimpleInvoice());

        doc.SourceDocumentKind.Should().Be("I");
        doc.Number.Should().Be("B-2026-0001");
        doc.SourceReference.Should().Be("demoerpb:B-2026-0001");
        doc.Supplier.Should().BeNull("l'émetteur n'est plus porté par l'agent — la plateforme le remplit à l'ingestion (ADR-0031 amendé)");
        doc.OperationCategory.Should().BeNull("la nature d'opération est remplie par la plateforme depuis le paramétrage fiscal du tenant");
        doc.Totals.TotalNet.Should().Be(100.00m);
        doc.Totals.TotalTax.Should().Be(20.00m);
        doc.Totals.TotalGross.Should().Be(120.00m);
        doc.Lines[0].SourceRegimeCodes.Should().Equal("20");
        doc.Lines[0].Taxes[0].CategoryCode.Should().BeNull();
        doc.Lines[0].Taxes[0].VatexCode.Should().BeNull();
        doc.CreditNoteRefs.Should().BeEmpty();
    }

    [Fact]
    public void Float_amounts_are_rounded_half_up_to_two_decimals()
    {
        DemoErpBInvoice invoice = SimpleInvoice();
        invoice.Items[0].VatAmount = 5.555; // float → decimal half-up → 5.56

        PivotDocumentDto doc = DemoErpBRowMapper.MapDocument(invoice);

        doc.Lines[0].Taxes[0].TaxAmount.Should().Be(5.56m);
    }

    [Fact]
    public void Credit_note_with_resolved_origin_carries_reference()
    {
        DemoErpBInvoice avoir = SimpleInvoice();
        avoir.InvoiceNo = "B-2026-0005";
        avoir.Kind = "C";
        avoir.OriginInvoiceNo = "B-2026-0001";
        avoir.OriginIssuedOn = new DateTime(2026, 6, 1);

        PivotDocumentDto doc = DemoErpBRowMapper.MapDocument(avoir);

        doc.SourceDocumentKind.Should().Be("C");
        doc.CreditNoteRefs.Should().HaveCount(1);
        doc.CreditNoteRefs[0].Number.Should().Be("B-2026-0001");
        doc.CreditNoteRefs[0].IssueDate.Should().Be(new DateTime(2026, 6, 1));
    }

    [Fact]
    public void Credit_note_without_resolved_origin_is_blocked()
    {
        DemoErpBInvoice avoir = SimpleInvoice();
        avoir.Kind = "C";
        avoir.OriginInvoiceNo = null;

        Action act = () => DemoErpBRowMapper.MapDocument(avoir);

        act.Should().Throw<SourceSchemaException>();
    }

    private static DemoErpBInvoice SimpleInvoice()
    {
        var invoice = new DemoErpBInvoice
        {
            InvoiceId = "1",
            InvoiceNo = "B-2026-0001",
            Kind = "I",
            IssuedOn = new DateTime(2026, 6, 1),
            Currency = "EUR",
            NetTotal = 100.0,
            VatTotal = 20.0,
            GrossTotal = 120.0,
            BuyerName = "Customer One",
            BuyerIsCompany = false,
        };
        invoice.Items.Add(new DemoErpBItem
        {
            LineNumber = "1",
            Label = "Demo service",
            Qty = 1.0,
            UnitPrice = 100.0,
            NetAmount = 100.0,
            VatAmount = 20.0,
            VatRate = 20.0,
            VatRegime = "20",
        });
        return invoice;
    }
}
