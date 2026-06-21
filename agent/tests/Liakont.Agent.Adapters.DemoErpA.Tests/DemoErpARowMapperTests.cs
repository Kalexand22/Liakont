namespace Liakont.Agent.Adapters.DemoErpA.Tests;

using System;
using FluentAssertions;
using Liakont.Agent.Adapters.DemoErpA;
using Liakont.Agent.Adapters.DemoErpA.Source;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Core.Extraction;
using Xunit;

/// <summary>
/// Transformation facture DemoErpA (decimal) → pivot EN 16931 : montants <c>decimal</c>, régimes BRUTS,
/// <c>CategoryCode</c>/<c>VatexCode</c> nuls (R3), type de pièce brut, lien d'avoir, B2C anonyme, et
/// blocage (jamais deviné) sur date ou origine d'avoir manquante (ADR-0004 D3-3, CLAUDE.md n°3).
/// </summary>
public class DemoErpARowMapperTests
{
    [Fact]
    public void Maps_invoice_with_decimal_amounts_and_raw_regime()
    {
        PivotDocumentDto doc = DemoErpARowMapper.MapDocument(SimpleInvoice());

        doc.SourceDocumentKind.Should().Be("FAC");
        doc.Number.Should().Be("A-2026-0001");
        doc.SourceReference.Should().Be("demoerpa:A-2026-0001");
        doc.Supplier.Should().BeNull("l'émetteur n'est plus porté par l'agent — la plateforme le remplit à l'ingestion (ADR-0031 amendé)");
        doc.OperationCategory.Should().BeNull("la nature d'opération est remplie par la plateforme depuis le paramétrage fiscal du tenant");
        doc.Totals.TotalNet.Should().Be(100.00m);
        doc.Totals.TotalTax.Should().Be(20.00m);
        doc.Totals.TotalGross.Should().Be(120.00m);
        doc.Lines.Should().HaveCount(1);
        doc.Lines[0].SourceRegimeCodes.Should().Equal("20");
        doc.Lines[0].Taxes[0].CategoryCode.Should().BeNull();
        doc.Lines[0].Taxes[0].VatexCode.Should().BeNull();
        doc.Lines[0].Taxes[0].TaxAmount.Should().Be(20.00m);
        doc.CreditNoteRefs.Should().BeEmpty();
    }

    [Fact]
    public void Maps_b2c_customer_without_company_hint()
    {
        PivotDocumentDto doc = DemoErpARowMapper.MapDocument(SimpleInvoice());

        doc.Customer.Should().NotBeNull();
        doc.Customer!.Name.Should().Be("Jean Dupont");
        doc.Customer.IsCompanyHint.Should().BeFalse();
        doc.Customer.Siren.Should().BeNull();
    }

    [Fact]
    public void Credit_note_with_resolved_origin_carries_reference()
    {
        DemoErpAInvoice avoir = SimpleInvoice();
        avoir.Numero = "A-2026-0005";
        avoir.TypePiece = "AVO";
        avoir.FactureOrigineNumero = "A-2026-0001";
        avoir.OrigineDate = new DateTime(2026, 6, 1);

        PivotDocumentDto doc = DemoErpARowMapper.MapDocument(avoir);

        doc.SourceDocumentKind.Should().Be("AVO");
        doc.CreditNoteRefs.Should().HaveCount(1);
        doc.CreditNoteRefs[0].Number.Should().Be("A-2026-0001");
        doc.CreditNoteRefs[0].IssueDate.Should().Be(new DateTime(2026, 6, 1));
        doc.CreditNoteRefs[0].SourceReference.Should().Be("demoerpa:A-2026-0001");
    }

    [Fact]
    public void Credit_note_without_resolved_origin_is_blocked()
    {
        DemoErpAInvoice avoir = SimpleInvoice();
        avoir.TypePiece = "AVO";
        avoir.FactureOrigineNumero = null;

        Action act = () => DemoErpARowMapper.MapDocument(avoir);

        act.Should().Throw<SourceSchemaException>();
    }

    [Fact]
    public void Missing_issue_date_is_blocked()
    {
        DemoErpAInvoice invoice = SimpleInvoice();
        invoice.DateEmission = default(DateTime);

        Action act = () => DemoErpARowMapper.MapDocument(invoice);

        act.Should().Throw<SourceSchemaException>();
    }

    private static DemoErpAInvoice SimpleInvoice()
    {
        var invoice = new DemoErpAInvoice
        {
            FactureId = "1",
            Numero = "A-2026-0001",
            TypePiece = "FAC",
            DateEmission = new DateTime(2026, 6, 1),
            Devise = "EUR",
            TotalHt = 100.00m,
            TotalTva = 20.00m,
            TotalTtc = 120.00m,
            ClientNom = "Jean Dupont",
            ClientEstSociete = false,
        };
        invoice.Lignes.Add(new DemoErpALine
        {
            NoLigne = "1",
            Designation = "Prestation de démonstration",
            Quantite = 1m,
            PrixUnitaire = 100.00m,
            MontantHt = 100.00m,
            MontantTva = 20.00m,
            TauxTva = 20.0m,
            CodeRegime = "20",
        });
        return invoice;
    }
}
