namespace Liakont.Agent.Adapters.EncheresV6.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Extraction;
using Xunit;

/// <summary>
/// Tests bout-en-bout de l'extracteur de fixtures EncheresV6 : rejeu de fichiers JSON au format
/// source → documents pivot valides, empreinte canonique stable (acceptance ADP01). Les fixtures
/// sont copiées en sortie de build et lues depuis <see cref="AppContext.BaseDirectory"/>.
/// </summary>
public class EncheresV6FixtureExtractorTests
{
    private static readonly DateTime PeriodFrom = new DateTime(2026, 1, 1);
    private static readonly DateTime PeriodTo = new DateTime(2026, 3, 1);

    private static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "fixtures", "encheresv6");

    private static string SalesFile => Path.Combine(FixturesDirectory, "encheresv6-source.json");

    [Fact]
    public void SourceName_is_EncheresV6()
    {
        SalesExtractor().SourceName.Should().Be("EncheresV6");
    }

    [Fact]
    public void Capabilities_reflect_the_easy_EncheresV6_case()
    {
        ExtractorCapabilities caps = SalesExtractor().Capabilities;

        caps.HasDetailedLines.Should().BeTrue();
        caps.HasCreditNoteLink.Should().BeTrue();
        caps.ExposesPayments.Should().BeTrue();
        caps.HasStoredHeaderTotal.Should().BeTrue();
        caps.RegimeKeyShape.Should().Be(RegimeKeyShape.Simple);
        caps.EmitterIdentitySource.Should().Be(EmitterIdentitySource.FromConfig);
        caps.ProvidesSourceDocuments.Should().BeFalse("la capacité PDF arrive avec ADP05");
        caps.ProvidesUnlinkedDocumentPool.Should().BeFalse();
    }

    [Fact]
    public void CheckHealth_is_healthy()
    {
        SalesExtractor().CheckHealth().IsHealthy.Should().BeTrue();
    }

    [Fact]
    public void ExtractDocuments_returns_the_three_sales_as_raw_kind_B()
    {
        List<PivotDocumentDto> docs = SalesExtractor().ExtractDocuments(PeriodFrom, PeriodTo).ToList();

        docs.Should().HaveCount(3);
        docs.Select(d => d.SourceReference).Should().BeEquivalentTo("no_ba=4500", "no_ba=4042", "no_ba=4501");
        docs.Should().OnlyContain(d => d.SourceDocumentKind == "B");
    }

    [Fact]
    public void ExtractDocuments_operation_category_comes_from_config()
    {
        EncheresV6FixtureExtractor extractor =
            EncheresV6FixtureExtractor.FromFile(SalesFile, Emitter(), OperationCategory.PrestationServices);

        extractor.ExtractDocuments(PeriodFrom, PeriodTo)
            .Should().OnlyContain(d => d.OperationCategory == OperationCategory.PrestationServices);
    }

    [Fact]
    public void ExtractDocuments_does_not_map_tva_and_keeps_regime_raw()
    {
        PivotDocumentDto sale = SalesExtractor().ExtractDocuments(PeriodFrom, PeriodTo)
            .Single(d => d.SourceReference == "no_ba=4500");

        sale.Lines.Should().HaveCount(2, "la ligne de règlement type 3 n'est pas une ligne de document");
        PivotLineDto adjudication = sale.Lines[0];
        adjudication.SourceRegimeCodes.Should().ContainSingle().Which.Should().Be("5");
        adjudication.Taxes[0].CategoryCode.Should().BeNull();
        adjudication.Taxes[0].VatexCode.Should().BeNull();
        sale.Supplier.Siren.Should().Be("111111111");
    }

    [Fact]
    public void ExtractDocuments_rounds_dirty_floats_half_up()
    {
        PivotDocumentDto sale = SalesExtractor().ExtractDocuments(PeriodFrom, PeriodTo)
            .Single(d => d.SourceReference == "no_ba=4500");

        PivotLineDto fees = sale.Lines[1];
        fees.NetAmount.Should().Be(8.33m);
        fees.Taxes[0].TaxAmount.Should().Be(1.67m);
    }

    [Fact]
    public void ExtractDocuments_marge_sale_carries_regime_6_with_zero_vat()
    {
        PivotDocumentDto marge = SalesExtractor().ExtractDocuments(PeriodFrom, PeriodTo)
            .Single(d => d.SourceReference == "no_ba=4042");

        PivotLineDto margeLine = marge.Lines.Single(l => l.SourceRegimeCodes.Contains("6"));
        margeLine.Taxes[0].TaxAmount.Should().Be(0m);
        margeLine.Taxes[0].Rate.Should().Be(0m);
        margeLine.Taxes[0].CategoryCode.Should().BeNull("la catégorie E/VATEX du régime de marge est mappée par la plateforme");
    }

    [Fact]
    public void ExtractDocuments_pro_buyer_sets_company_hint()
    {
        List<PivotDocumentDto> docs = SalesExtractor().ExtractDocuments(PeriodFrom, PeriodTo).ToList();

        docs.Single(d => d.SourceReference == "no_ba=4501").Customer!.IsCompanyHint.Should().BeTrue();
        docs.Single(d => d.SourceReference == "no_ba=4500").Customer!.IsCompanyHint.Should().BeFalse();
    }

    [Fact]
    public void ExtractDocuments_is_idempotent()
    {
        EncheresV6FixtureExtractor extractor = SalesExtractor();

        IEnumerable<string> first = extractor.ExtractDocuments(PeriodFrom, PeriodTo).Select(d => d.SourceReference);
        IEnumerable<string> second = extractor.ExtractDocuments(PeriodFrom, PeriodTo).Select(d => d.SourceReference);

        first.Should().Equal(second);
    }

    [Fact]
    public void ExtractDocuments_filters_by_period()
    {
        List<PivotDocumentDto> firstHalf = SalesExtractor()
            .ExtractDocuments(new DateTime(2026, 1, 1), new DateTime(2026, 1, 15))
            .ToList();

        firstHalf.Select(d => d.SourceReference).Should().BeEquivalentTo("no_ba=4500", "no_ba=4042");
    }

    [Fact]
    public void ExtractPayments_returns_raw_payment_from_type3_line()
    {
        List<PivotPaymentDto> payments = SalesExtractor().ExtractPayments(PeriodFrom, PeriodTo).ToList();

        payments.Should().ContainSingle();
        PivotPaymentDto payment = payments[0];
        payment.PaymentDate.Should().Be(new DateTime(2026, 1, 15));
        payment.Amount.Should().Be(130.00m);
        payment.Method.Should().Be("CB");
        payment.RelatedDocumentNumber.Should().Be("F-2026-0500");
        payment.SourceReference.Should().Be("no_remise=REM-0500");
    }

    [Fact]
    public void ListSourceTaxRegimes_returns_regimes_with_occurrences()
    {
        IReadOnlyList<SourceTaxRegimeDto> regimes = SalesExtractor().ListSourceTaxRegimes();

        regimes.Should().HaveCount(2);
        regimes.Single(r => r.Code == "5").Occurrences.Should().Be(5);
        regimes.Single(r => r.Code == "6").Occurrences.Should().Be(1);
        regimes.Single(r => r.Code == "6").Label.Should().Be("Regime de la marge");
    }

    [Fact]
    public void GetAttachments_and_pool_are_empty_in_fixture_mode()
    {
        EncheresV6FixtureExtractor extractor = SalesExtractor();

        extractor.GetAttachments("no_ba=4500").Should().BeEmpty();
        extractor.ListPoolDocuments(PeriodFrom, PeriodTo).Should().BeEmpty();
    }

    [Fact]
    public void FromDirectory_merges_files_and_resolves_credit_note_across_files()
    {
        EncheresV6FixtureExtractor extractor =
            EncheresV6FixtureExtractor.FromDirectory(FixturesDirectory, Emitter(), OperationCategory.LivraisonBiens);

        List<PivotDocumentDto> docs = extractor.ExtractDocuments(PeriodFrom, PeriodTo).ToList();
        docs.Should().HaveCount(4, "3 ventes + 1 avoir (fichier séparé)");

        PivotDocumentDto avoir = docs.Single(d => d.SourceDocumentKind == "A");
        avoir.CreditNoteRefs.Should().ContainSingle();
        avoir.CreditNoteRefs[0].Number.Should().Be("F-2026-0500");
        avoir.CreditNoteRefs[0].IssueDate.Should().Be(new DateTime(2026, 1, 12));
        avoir.CreditNoteRefs[0].SourceReference.Should().Be("no_ba=4500");
    }

    [Fact]
    public void Canonical_hash_is_stable_across_two_independent_extractions()
    {
        PivotDocumentDto first = SalesExtractor().ExtractDocuments(PeriodFrom, PeriodTo)
            .Single(d => d.SourceReference == "no_ba=4500");
        PivotDocumentDto second = SalesExtractor().ExtractDocuments(PeriodFrom, PeriodTo)
            .Single(d => d.SourceReference == "no_ba=4500");

        string hashFirst = PayloadHasher.ComputeHash(first);
        string hashSecond = PayloadHasher.ComputeHash(second);

        hashFirst.Should().Be(hashSecond, "le même bordereau doit produire une empreinte identique (idempotence + canonicalisation)");
        Regex.IsMatch(hashFirst, "^[0-9a-f]{64}$").Should().BeTrue("empreinte SHA-256 en hexadécimal minuscule de 64 caractères");
        CanonicalJson.Serialize(first).Should().Be(CanonicalJson.Serialize(second));
    }

    [Fact]
    public void Canonical_hash_differs_between_distinct_documents()
    {
        List<PivotDocumentDto> docs = SalesExtractor().ExtractDocuments(PeriodFrom, PeriodTo).ToList();

        string hashSale = PayloadHasher.ComputeHash(docs.Single(d => d.SourceReference == "no_ba=4500"));
        string hashMarge = PayloadHasher.ComputeHash(docs.Single(d => d.SourceReference == "no_ba=4042"));

        hashSale.Should().NotBe(hashMarge);
    }

    private static EncheresV6EmitterIdentity Emitter() =>
        new EncheresV6EmitterIdentity(
            name: "Étude Fictïve SVV",
            siren: "111111111",
            city: "Rennes",
            postalCode: "35000",
            countryCode: "FR");

    private static EncheresV6FixtureExtractor SalesExtractor() =>
        EncheresV6FixtureExtractor.FromFile(SalesFile, Emitter(), OperationCategory.LivraisonBiens);
}
