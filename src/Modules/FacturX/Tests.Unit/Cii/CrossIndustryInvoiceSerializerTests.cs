namespace Liakont.Modules.FacturX.Tests.Unit.Cii;

using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.FacturX.Application.Cii;
using Liakont.Modules.FacturX.Domain;
using Liakont.Modules.FacturX.Domain.Cii;
using Xunit;

/// <summary>
/// Tests unitaires du sérialiseur CII (FX03) : recopie des valeurs qualitatives, dérivation/réconciliation
/// des agrégats (BG-23, BT-106/115), et BLOCAGE (jamais de CII faux) sur BT manquant ou écart non
/// réconciliable (ADR-0023 INV-FX-2/7 ; CLAUDE.md n°2/3). La conformité XSD + BR-CO de toute la matrice
/// est couverte par <see cref="CrossIndustryInvoiceMatrixTests"/>.
/// </summary>
public sealed class CrossIndustryInvoiceSerializerTests
{
    private static readonly XNamespace Ram = CiiProfile.RamNamespace;
    private static readonly string[] ExpectedMultiTauxRates = { "20", "5.5" };
    private readonly CrossIndustryInvoiceSerializer _serializer = new();

    [Fact]
    public void Serialize_NullPivot_Throws()
    {
        var act = () => _serializer.Serialize(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Serialize_MonoTaux_RecopiesQualitativeValuesAndProfileConstants()
    {
        var root = Parse(_serializer.Serialize(CiiTestPivots.Get("mono-taux")));

        // BT-24 (profil), BT-1 (numéro), BT-3 (type), BT-2 (date format 102).
        Guideline(root).Should().Be(CiiProfile.SpecificationIdentifier);
        ExchangedDoc(root).Element(Ram + "ID")!.Value.Should().Be("FAC-2026-0001");
        ExchangedDoc(root).Element(Ram + "TypeCode")!.Value.Should().Be("380");
        var dateString = ExchangedDoc(root).Element(Ram + "IssueDateTime")!
            .Descendants().First();
        dateString.Value.Should().Be("20260115");
        dateString.Attribute("format")!.Value.Should().Be("102");

        // Ligne : recopie BT-151 (catégorie), BT-152 (taux), BT-146 (PU), BT-129 (qté), BT-131 (net).
        var lineTax = LineTradeTax(root);
        lineTax.Element(Ram + "CategoryCode")!.Value.Should().Be("S");
        lineTax.Element(Ram + "RateApplicablePercent")!.Value.Should().Be("20");
        UnitPrice(root).Should().Be("100.00");
        var quantity = root.Descendants(Ram + "BilledQuantity").First();
        quantity.Value.Should().Be("1");
        quantity.Attribute("unitCode")!.Value.Should().Be(CiiProfile.DefaultUnitCode);

        // BG-23 dérivée : une ventilation S/20 ; BT-116 = 100,00 ; BT-117 = 20,00.
        var breakdown = HeaderTaxes(root);
        breakdown.Should().HaveCount(1);
        breakdown[0].Element(Ram + "BasisAmount")!.Value.Should().Be("100.00");
        breakdown[0].Element(Ram + "CalculatedAmount")!.Value.Should().Be("20.00");
        breakdown[0].Element(Ram + "CategoryCode")!.Value.Should().Be("S");

        // BT-5 devise + total TVA avec currencyID.
        Settlement(root).Element(Ram + "InvoiceCurrencyCode")!.Value.Should().Be("EUR");
        var taxTotal = Summation(root).Element(Ram + "TaxTotalAmount")!;
        taxTotal.Value.Should().Be("20.00");
        taxTotal.Attribute("currencyID")!.Value.Should().Be("EUR");
        Summation(root).Element(Ram + "GrandTotalAmount")!.Value.Should().Be("120.00");
    }

    [Fact]
    public void Serialize_MonoTaux_RecopiesSellerLegalAndVatIdentifiers()
    {
        var root = Parse(_serializer.Serialize(CiiTestPivots.Get("mono-taux")));
        var seller = root.Descendants(Ram + "SellerTradeParty").Single();

        seller.Element(Ram + "Name")!.Value.Should().Be("Boucherie Durand SARL");
        var legalId = seller.Element(Ram + "SpecifiedLegalOrganization")!.Element(Ram + "ID")!;
        legalId.Value.Should().Be("552100554");
        legalId.Attribute("schemeID")!.Value.Should().Be(CiiProfile.SirenScheme);
        var vatId = seller.Element(Ram + "SpecifiedTaxRegistration")!.Element(Ram + "ID")!;
        vatId.Value.Should().Be("FR55552100554");
        vatId.Attribute("schemeID")!.Value.Should().Be(CiiProfile.VatScheme);
        seller.Element(Ram + "PostalTradeAddress")!.Element(Ram + "CountryID")!.Value.Should().Be("FR");
    }

    [Fact]
    public void Serialize_MultiTaux_ProducesTwoVatBreakdownGroups()
    {
        var root = Parse(_serializer.Serialize(CiiTestPivots.Get("multi-taux")));
        var breakdown = HeaderTaxes(root);

        breakdown.Should().HaveCount(2);
        breakdown.Select(t => t.Element(Ram + "RateApplicablePercent")!.Value)
            .Should().BeEquivalentTo(ExpectedMultiTauxRates);

        // Σ BT-117 = 40,00 + 5,50 = 45,50 = BT-110.
        Summation(root).Element(Ram + "TaxTotalAmount")!.Value.Should().Be("45.50");
    }

    [Fact]
    public void Serialize_ExonereVatex_RecopiesExemptionReasonCode()
    {
        var root = Parse(_serializer.Serialize(CiiTestPivots.Get("exonere-vatex")));
        var tax = HeaderTaxes(root).Single();

        tax.Element(Ram + "CategoryCode")!.Value.Should().Be("E");
        tax.Element(Ram + "ExemptionReasonCode")!.Value.Should().Be("VATEX-FR-FRANCHISE");
        tax.Element(Ram + "CalculatedAmount")!.Value.Should().Be("0.00");
    }

    [Fact]
    public void Serialize_CrieeWithPrepaid_DerivesDuePayable()
    {
        var root = Parse(_serializer.Serialize(CiiTestPivots.Get("criee-mono-seller")));
        var summation = Summation(root);

        // Deux lignes S/20 agrégées en une seule ventilation BG-23.
        HeaderTaxes(root).Should().HaveCount(1);
        summation.Element(Ram + "GrandTotalAmount")!.Value.Should().Be("2880.00");
        summation.Element(Ram + "TotalPrepaidAmount")!.Value.Should().Be("880.00");

        // BT-115 = BT-112 − BT-113 = 2880,00 − 880,00.
        summation.Element(Ram + "DuePayableAmount")!.Value.Should().Be("2000.00");
    }

    [Fact]
    public void Serialize_LineWithoutUnitPrice_Blocks()
    {
        var pivot = SingleLinePivot(unitPrice: null, taxAmount: 20m, rate: 20m, category: VatCategory.S);

        var act = () => _serializer.Serialize(pivot);

        act.Should().Throw<FacturXGenerationException>().WithMessage("*BT-146*");
    }

    [Fact]
    public void Serialize_LineWithoutTaxBreakdown_Blocks()
    {
        var line = new PivotLineDto("Sans taxe", netAmount: 100m, unitPriceNet: 100m, taxes: Array.Empty<PivotLineTaxDto>());
        var pivot = PivotFrom(new[] { line }, totalNet: 100m, totalTax: 0m, totalGross: 100m);

        var act = () => _serializer.Serialize(pivot);

        act.Should().Throw<FacturXGenerationException>().WithMessage("*BG-30*");
    }

    [Fact]
    public void Serialize_LineWithoutCategory_Blocks()
    {
        var pivot = SingleLinePivot(unitPrice: 100m, taxAmount: 20m, rate: 20m, category: null);

        var act = () => _serializer.Serialize(pivot);

        act.Should().Throw<FacturXGenerationException>().WithMessage("*BT-151*");
    }

    [Fact]
    public void Serialize_WithDocumentCharges_Blocks()
    {
        var line = Line(100m, 100m, 20m, 20m, VatCategory.S);
        var charge = new PivotDocumentChargeDto(isCharge: true, amount: 10m);
        var pivot = new PivotDocumentDto(
            "INVOICE", "FAC-X", new DateTime(2026, 1, 15), "SRC", Supplier(),
            new PivotTotalsDto(100m, 20m, 120m), OperationCategory.LivraisonBiens,
            customer: Customer(), lines: new[] { line }, documentCharges: new[] { charge });

        var act = () => _serializer.Serialize(pivot);

        act.Should().Throw<FacturXGenerationException>().WithMessage("*BG-20*");
    }

    [Fact]
    public void Serialize_WithoutCustomer_Blocks()
    {
        var line = Line(100m, 100m, 20m, 20m, VatCategory.S);
        var pivot = new PivotDocumentDto(
            "INVOICE", "FAC-X", new DateTime(2026, 1, 15), "SRC", Supplier(),
            new PivotTotalsDto(100m, 20m, 120m), OperationCategory.LivraisonBiens,
            customer: null, lines: new[] { line });

        var act = () => _serializer.Serialize(pivot);

        act.Should().Throw<FacturXGenerationException>().WithMessage("*BR-07*");
    }

    [Fact]
    public void Serialize_SupplierWithoutCountry_Blocks()
    {
        var supplierNoCountry = new PivotPartyDto("Vendeur SARL", siren: "552100554", isCompanyHint: true);
        var line = Line(100m, 100m, 20m, 20m, VatCategory.S);
        var pivot = new PivotDocumentDto(
            "INVOICE", "FAC-X", new DateTime(2026, 1, 15), "SRC", supplierNoCountry,
            new PivotTotalsDto(100m, 20m, 120m), OperationCategory.LivraisonBiens,
            customer: Customer(), lines: new[] { line });

        var act = () => _serializer.Serialize(pivot);

        act.Should().Throw<FacturXGenerationException>().WithMessage("*BT-40*");
    }

    [Fact]
    public void Serialize_NonReconcilableVatTotal_Blocks()
    {
        // BT-110 (25) ≠ Σ BT-117 dérivé (round(100×20/100)=20) → BR-CO-14.
        var pivot = SingleLinePivot(unitPrice: 100m, taxAmount: 20m, rate: 20m, category: VatCategory.S,
            totalNet: 100m, totalTax: 25m, totalGross: 125m);

        var act = () => _serializer.Serialize(pivot);

        act.Should().Throw<FacturXGenerationException>().WithMessage("*BR-CO-14*");
    }

    [Fact]
    public void Serialize_NonReconcilableGrandTotal_Blocks()
    {
        // BT-112 (130) ≠ BT-109 + BT-110 (120) → BR-CO-15.
        var pivot = SingleLinePivot(unitPrice: 100m, taxAmount: 20m, rate: 20m, category: VatCategory.S,
            totalNet: 100m, totalTax: 20m, totalGross: 130m);

        var act = () => _serializer.Serialize(pivot);

        act.Should().Throw<FacturXGenerationException>().WithMessage("*BR-CO-15*");
    }

    [Fact]
    public void Serialize_LineTotalsNotMatchingTaxBasis_Blocks()
    {
        // BT-106 (Σ BT-131 = 100) ≠ BT-109 (110) → BR-CO-10/13.
        var pivot = SingleLinePivot(unitPrice: 100m, taxAmount: 22m, rate: 20m, category: VatCategory.S,
            totalNet: 110m, totalTax: 22m, totalGross: 132m);

        var act = () => _serializer.Serialize(pivot);

        act.Should().Throw<FacturXGenerationException>().WithMessage("*BR-CO-10/13*");
    }

    [Fact]
    public void Serialize_WithBillingMentions_EmitsPaymentTermsDueDateAndNote()
    {
        // BUG-26 (F16 §3.5) : un pivot portant les mentions de facturation (BT-20 termes de paiement, note
        // légale FR PMD) + une échéance (BT-9) émet ram:SpecifiedTradePaymentTerms (Description + DueDateDateTime)
        // et un ram:IncludedNote (Content + SubjectCode). Contenus = mentions tenant recopiées, rien d'inventé.
        var pivot = SingleLineWithMentions(
            paymentTerms: "Paiement à 30 jours fin de mois.",
            notes: new[] { new PivotDocumentNoteDto("Pénalités de retard au taux légal.", "PMD") },
            deliveryDate: new DateTime(2026, 1, 20),
            paymentDueDate: new DateTime(2026, 2, 15));

        var root = Parse(_serializer.Serialize(pivot));

        // BT-20 (Description) + BT-9 (DueDateDateTime, format 102 = yyyyMMdd) dans les termes de paiement.
        var terms = Settlement(root).Element(Ram + "SpecifiedTradePaymentTerms")!;
        terms.Element(Ram + "Description")!.Value.Should().Be("Paiement à 30 jours fin de mois.");
        var dueDate = terms.Element(Ram + "DueDateDateTime")!.Descendants().First();
        dueDate.Value.Should().Be("20260215");
        dueDate.Attribute("format")!.Value.Should().Be("102");

        // BG-1 (IncludedNote) : Content (BT-22) puis SubjectCode (BT-21) = « PMD ».
        var note = ExchangedDoc(root).Element(Ram + "IncludedNote")!;
        note.Element(Ram + "Content")!.Value.Should().Be("Pénalités de retard au taux légal.");
        note.Element(Ram + "SubjectCode")!.Value.Should().Be("PMD");
    }

    [Fact]
    public void Serialize_WithDeliveryDate_EmitsNonEmptyDeliveryWithOccurrenceDate()
    {
        // BUG-26 (R008) : la date de livraison portée par le pivot (BT-72) alimente
        // ram:ActualDeliverySupplyChainEvent/OccurrenceDateTime — l'élément livraison n'est jamais vide.
        var pivot = SingleLineWithMentions(deliveryDate: new DateTime(2026, 1, 20));

        var root = Parse(_serializer.Serialize(pivot));

        var occurrence = Delivery(root)
            .Element(Ram + "ActualDeliverySupplyChainEvent")!
            .Element(Ram + "OccurrenceDateTime")!
            .Descendants().First();
        occurrence.Value.Should().Be("20260120", "la date de livraison portée par le pivot est recopiée (BT-72)");
        occurrence.Attribute("format")!.Value.Should().Be("102");
    }

    [Fact]
    public void Serialize_WithoutDeliveryDate_StillEmitsNonEmptyDeliveryFromIssueDate()
    {
        // BUG-26 (R008) : sans date de livraison portée, l'élément livraison reste NON vide — il retombe sur la
        // date d'émission (BT-2), toujours présente. PEPPOL-EN16931-R008 interdit un élément livraison vide.
        var pivot = SingleLineWithMentions(deliveryDate: null);

        var root = Parse(_serializer.Serialize(pivot));

        var delivery = Delivery(root);
        delivery.Elements().Should().NotBeEmpty("l'élément livraison n'est jamais vide (PEPPOL-EN16931-R008)");
        var occurrence = delivery
            .Element(Ram + "ActualDeliverySupplyChainEvent")!
            .Element(Ram + "OccurrenceDateTime")!
            .Descendants().First();
        occurrence.Value.Should().Be("20260115", "sans date de livraison, l'élément retombe sur la date d'émission (BT-2)");
    }

    [Fact]
    public void Serialize_LineWithUnitCode_ProjectsItOnBilledQuantity()
    {
        // RD407 (BT-130) : quand le pivot porte une unité, le CII l'émet telle quelle (codes UN/ECE Rec 20),
        // sinon C62 (cf. Serialize_MonoTaux…). La quantité réelle BT-129 reste émise — unité ↔ quantité cohérentes.
        var line = new PivotLineDto(
            "Article", netAmount: 100m, quantity: 5m, unitPriceNet: 20m,
            taxes: new[] { new PivotLineTaxDto(20m, 20m, VatCategory.S) }, unitCode: "KGM");
        var pivot = PivotFrom(new[] { line }, totalNet: 100m, totalTax: 20m, totalGross: 120m);

        var root = Parse(_serializer.Serialize(pivot));

        var quantity = root.Descendants(Ram + "BilledQuantity").First();
        quantity.Attribute("unitCode")!.Value.Should().Be("KGM", "l'unité portée par le pivot est recopiée telle quelle");
        quantity.Value.Should().Be("5", "la quantité réelle BT-129 du pivot est émise");
    }

    private static PivotDocumentDto SingleLinePivot(
        decimal? unitPrice,
        decimal taxAmount,
        decimal? rate,
        VatCategory? category,
        decimal totalNet = 100m,
        decimal totalTax = 20m,
        decimal totalGross = 120m)
    {
        var tax = new PivotLineTaxDto(taxAmount, rate, category);
        var line = new PivotLineDto("Article", netAmount: 100m, unitPriceNet: unitPrice, taxes: new[] { tax });
        return PivotFrom(new[] { line }, totalNet, totalTax, totalGross);
    }

    private static PivotLineDto Line(decimal net, decimal unitPrice, decimal rate, decimal taxAmount, VatCategory category) =>
        new("Article", netAmount: net, unitPriceNet: unitPrice, taxes: new[] { new PivotLineTaxDto(taxAmount, rate, category) });

    // Pivot mono-taux S/20 réconciliable (100,00 / 20,00 / 120,00) portant les champs additifs BUG-26
    // (termes de paiement BT-20, notes BG-1, date de livraison BT-72, échéance BT-9). Tous fictifs (n°7).
    private static PivotDocumentDto SingleLineWithMentions(
        string? paymentTerms = null,
        IReadOnlyList<PivotDocumentNoteDto>? notes = null,
        DateTime? deliveryDate = null,
        DateTime? paymentDueDate = null)
    {
        var line = Line(100m, 100m, 20m, 20m, VatCategory.S);
        return new PivotDocumentDto(
            "INVOICE", "FAC-X", new DateTime(2026, 1, 15), "SRC", Supplier(),
            new PivotTotalsDto(100m, 20m, 120m), OperationCategory.LivraisonBiens,
            customer: Customer(), lines: new[] { line },
            paymentDueDate: paymentDueDate, paymentTerms: paymentTerms, notes: notes, deliveryDate: deliveryDate);
    }

    private static PivotDocumentDto PivotFrom(
        IReadOnlyList<PivotLineDto> lines, decimal totalNet, decimal totalTax, decimal totalGross) =>
        new(
            "INVOICE", "FAC-X", new DateTime(2026, 1, 15), "SRC", Supplier(),
            new PivotTotalsDto(totalNet, totalTax, totalGross), OperationCategory.LivraisonBiens,
            customer: Customer(), lines: lines);

    private static PivotPartyDto Supplier() =>
        new("Vendeur SARL", siren: "552100554", address: new PivotAddressDto(countryCode: "FR"), isCompanyHint: true);

    private static PivotPartyDto Customer() =>
        new("Client", address: new PivotAddressDto(countryCode: "FR"));

    private static XElement Parse(byte[] xml) => XDocument.Parse(Encoding.UTF8.GetString(xml)).Root!;

    private static string Guideline(XElement root) => root
        .Descendants(Ram + "GuidelineSpecifiedDocumentContextParameter").Single()
        .Element(Ram + "ID")!.Value;

    private static XElement ExchangedDoc(XElement root) =>
        root.Elements().Single(e => e.Name.LocalName == "ExchangedDocument");

    private static XElement Settlement(XElement root) =>
        root.Descendants(Ram + "ApplicableHeaderTradeSettlement").Single();

    private static XElement Delivery(XElement root) =>
        root.Descendants(Ram + "ApplicableHeaderTradeDelivery").Single();

    private static XElement Summation(XElement root) =>
        Settlement(root).Element(Ram + "SpecifiedTradeSettlementHeaderMonetarySummation")!;

    private static List<XElement> HeaderTaxes(XElement root) =>
        Settlement(root).Elements(Ram + "ApplicableTradeTax").ToList();

    private static XElement LineTradeTax(XElement root) =>
        root.Descendants(Ram + "SpecifiedLineTradeSettlement").First().Element(Ram + "ApplicableTradeTax")!;

    private static string UnitPrice(XElement root) => root
        .Descendants(Ram + "NetPriceProductTradePrice").First()
        .Element(Ram + "ChargeAmount")!.Value;
}
