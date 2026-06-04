namespace Liakont.Agent.Contracts.Tests.Unit;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Xunit;

/// <summary>
/// Tests de construction du modèle pivot (acceptance PIV01) : un document complet (avoir groupé +
/// lignes + taxes + paiements + charges) se construit et restitue fidèlement ses valeurs ; les
/// valeurs par défaut (devise EUR, collections vides, quantité 1) sont sûres.
/// </summary>
public sealed class PivotDocumentDtoTests
{
    [Fact]
    public void Constructor_Should_Assign_Mandatory_Fields_And_Default_The_Rest()
    {
        var supplier = new PivotPartyDto("Galerie Fictive SARL", siren: "111111111");
        var totals = new PivotTotalsDto(totalNet: 100m, totalTax: 20m, totalGross: 120m);
        var issueDate = new DateTime(2026, 1, 15);

        var doc = new PivotDocumentDto(
            sourceDocumentKind: "B",
            number: "F-2026-001",
            issueDate: issueDate,
            sourceReference: "no_ba=4242",
            supplier: supplier,
            totals: totals,
            operationCategory: OperationCategory.LivraisonBiens);

        doc.SourceDocumentKind.Should().Be("B");
        doc.Number.Should().Be("F-2026-001");
        doc.IssueDate.Should().Be(issueDate);
        doc.SourceReference.Should().Be("no_ba=4242");
        doc.Supplier.Should().BeSameAs(supplier);
        doc.Totals.Should().BeSameAs(totals);
        doc.OperationCategory.Should().Be(OperationCategory.LivraisonBiens);

        // Valeurs par défaut sûres.
        doc.CurrencyCode.Should().Be("EUR");
        doc.Customer.Should().BeNull();
        doc.Lines.Should().BeEmpty();
        doc.CreditNoteRefs.Should().BeEmpty();
        doc.Payments.Should().BeEmpty();
        doc.DocumentCharges.Should().BeEmpty();
        doc.Invoicer.Should().BeNull();
        doc.Payee.Should().BeNull();
        doc.IsSelfBilled.Should().BeFalse();
        doc.PrepaidAmount.Should().BeNull();
        doc.SourceData.Should().BeNull();
    }

    [Fact]
    public void Constructor_Should_Build_A_Full_Grouped_Credit_Note()
    {
        var sourceRegimes = new[] { "6", "MARGE" };
        var line = new PivotLineDto(
            description: "Adjudication lot 12",
            netAmount: 1000m,
            quantity: 1m,
            unitPriceNet: 1000m,
            sourceRegimeCodes: sourceRegimes,
            taxes: new[] { new PivotLineTaxDto(taxAmount: 0m, rate: 0m, categoryCode: VatCategory.E, vatexCode: "VATEX-EU-J") },
            sourceLineRef: "ligne#4");

        var creditRefs = new[]
        {
            new PivotDocumentRefDto("F-2026-001", new DateTime(2026, 1, 15)),
            new PivotDocumentRefDto("F-2026-002", new DateTime(2026, 1, 16), sourceReference: "no_ba=99"),
        };

        var doc = new PivotDocumentDto(
            sourceDocumentKind: "A",
            number: "AV-2026-009",
            issueDate: new DateTime(2026, 2, 1),
            sourceReference: "no_ba=5000",
            supplier: new PivotPartyDto("Galerie Fictive SARL", siren: "111111111"),
            totals: new PivotTotalsDto(1000m, 0m, 1000m, sourceTotalGross: 1000m),
            operationCategory: OperationCategory.Mixte,
            customer: new PivotPartyDto("Acheteur Pro Fictif", isCompanyHint: true),
            lines: new[] { line },
            creditNoteRefs: creditRefs,
            payments: new[] { new PivotPaymentDto(new DateTime(2026, 2, 2), 1000m, method: "CB") },
            documentCharges: new[] { new PivotDocumentChargeDto(isCharge: true, amount: 12.50m, reason: "éco-contribution") },
            isSelfBilled: true,
            prepaidAmount: 300m,
            sourceData: "{\"raw\":true}");

        doc.Lines.Should().ContainSingle();
        doc.Lines[0].SourceRegimeCodes.Should().Equal("6", "MARGE");
        doc.Lines[0].Taxes.Should().ContainSingle();
        doc.Lines[0].Taxes[0].CategoryCode.Should().Be(VatCategory.E);
        doc.Lines[0].Taxes[0].VatexCode.Should().Be("VATEX-EU-J");

        doc.CreditNoteRefs.Should().HaveCount(2);
        doc.CreditNoteRefs[0].Number.Should().Be("F-2026-001");
        doc.CreditNoteRefs[0].IssueDate.Should().Be(new DateTime(2026, 1, 15));
        doc.CreditNoteRefs[1].SourceReference.Should().Be("no_ba=99");

        doc.Payments.Should().ContainSingle();
        doc.DocumentCharges.Should().ContainSingle();
        doc.DocumentCharges[0].IsCharge.Should().BeTrue();
        doc.IsSelfBilled.Should().BeTrue();
        doc.PrepaidAmount.Should().Be(300m);
        doc.SourceData.Should().Be("{\"raw\":true}");
    }

    [Fact]
    public void PivotLine_Should_Default_Quantity_To_One_And_Collections_To_Empty()
    {
        var line = new PivotLineDto(description: "Frais", netAmount: 50m);

        line.Quantity.Should().Be(1m);
        line.UnitPriceNet.Should().BeNull();
        line.SourceRegimeCodes.Should().BeEmpty();
        line.Taxes.Should().BeEmpty();
    }

    [Fact]
    public void SourceRegimeCodes_Is_A_Collection_Not_A_Single_String()
    {
        // ADR-0004 D3-1 : le régime source est une COLLECTION par ligne (couple NAV, multi-taxes
        // Axelor), jamais une simple chaîne.
        var property = typeof(PivotLineDto).GetProperty(nameof(PivotLineDto.SourceRegimeCodes));

        property!.PropertyType.Should().Be<IReadOnlyList<string>>();
    }

    [Fact]
    public void PivotParty_Should_Default_IsCompanyHint_To_False_With_No_Heuristic()
    {
        var party = new PivotPartyDto("Client Fictif");

        party.IsCompanyHint.Should().BeFalse();
        party.Siren.Should().BeNull();
        party.Address.Should().BeNull();
    }
}
