namespace Liakont.Modules.FacturX.Tests.Unit;

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.FacturX.Application.Cii;
using Liakont.Modules.FacturX.Contracts;
using Liakont.Modules.FacturX.Domain;
using Liakont.Modules.FacturX.Infrastructure;
using Liakont.Modules.FacturX.Tests.Unit.Cii;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Xunit;

/// <summary>
/// Tests du scellement Factur-X (FX04, ADR-0023 §3/§4 ; F16 §4-5). Vérifie au tier rapide
/// (assertions structurelles, pas de Docker) : (1) un PDF/A-3 est produit avec le <c>factur-x.xml</c>
/// embarqué dont le contenu est exactement le CII du sérialiseur ; (2) le bloc XMP <c>fx:</c> + le
/// marqueur PDF/A-3 (<c>pdfaid:part 3</c>) sont écrits ; (3) la COHÉRENCE visuel↔XML — le PDF lisible et
/// le CII portent les MÊMES montants ; (4) le blocage du sérialiseur est propagé (fail-closed). La
/// conformité PDF/A-3b / EN 16931 RÉELLE est validée par veraPDF + Mustang au tier intégration
/// (Tests.Integration, INV-FX-5) — ces deux outils sont la source de vérité pour <c>/AFRelationship</c>
/// et le Schematron EN 16931, que PdfPig n'expose pas (F16 §8).
/// </summary>
public sealed class FacturXBuilderTests
{
    static FacturXBuilderTests()
    {
        // QuestPDF exige l'activation de la licence avant tout rendu. En production c'est
        // AddFacturXModule ; ici on appelle le builder en direct, donc on la pose explicitement.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    public static IEnumerable<object[]> MatrixCases =>
        CiiTestPivots.Names.Select(name => new object[] { name });

    private static FacturXBuilder CreateBuilder() =>
        new(new CrossIndustryInvoiceSerializer());

    [Theory]
    [MemberData(nameof(MatrixCases))]
    public async Task BuildAsync_EmbedsCrossIndustryInvoiceXml(string caseName)
    {
        PivotDocumentDto pivot = CiiTestPivots.Get(caseName);

        FacturXDocument result = await CreateBuilder().BuildAsync(pivot);

        result.PdfBytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(result.PdfBytes, 0, 5).Should().Be("%PDF-", "l'artefact doit être un PDF");
        result.FileName.Should().Be(pivot.Number + ".pdf");
        result.CrossIndustryInvoiceXml.Should().NotBeEmpty();

        using PdfDocument document = PdfDocument.Open(result.PdfBytes);
        document.Advanced.TryGetEmbeddedFiles(out IReadOnlyList<EmbeddedFile>? files)
            .Should().BeTrue("le Factur-X embarque le factur-x.xml (AFRelationship Alternative)");
        EmbeddedFile facturX = files!
            .Should().ContainSingle(f => f.FileSpecification == "factur-x.xml" || f.Name == "factur-x.xml")
            .Subject;
        facturX.Bytes.ToArray().Should().Equal(
            result.CrossIndustryInvoiceXml,
            "le XML embarqué est exactement le CII du sérialiseur (jamais régénéré)");
    }

    [Theory]
    [MemberData(nameof(MatrixCases))]
    public async Task BuildAsync_WritesFacturXXmpAndPdfAMarker(string caseName)
    {
        PivotDocumentDto pivot = CiiTestPivots.Get(caseName);

        FacturXDocument result = await CreateBuilder().BuildAsync(pivot);

        using PdfDocument document = PdfDocument.Open(result.PdfBytes);
        document.TryGetXmpMetadata(out XmpMetadata? xmp).Should().BeTrue("un PDF/A-3 porte un flux XMP");
        string metadata = Encoding.UTF8.GetString(xmp!.GetXmlBytes().ToArray());

        metadata.Should().Contain("urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#", "URI d'extension fx: (INV-FX-3)");
        metadata.Should().Contain("<fx:ConformanceLevel>EN 16931</fx:ConformanceLevel>");
        metadata.Should().Contain("<fx:DocumentType>INVOICE</fx:DocumentType>");
        metadata.Should().Contain("<fx:DocumentFileName>factur-x.xml</fx:DocumentFileName>");
        metadata.Should().Contain("<fx:Version>1.0</fx:Version>");

        // Marqueur PDF/A-3 posé par QuestPDF (DocumentSettings.PdfA) : pdfaid:part = 3.
        Regex.IsMatch(metadata, @"pdfaid:part>\s*3\s*<|pdfaid:part=""3""")
            .Should().BeTrue("le PDF doit être déclaré PDF/A partie 3 dans le XMP");
    }

    [Theory]
    [MemberData(nameof(MatrixCases))]
    public async Task BuildAsync_VisualAndXmlCarrySameAmounts(string caseName)
    {
        PivotDocumentDto pivot = CiiTestPivots.Get(caseName);

        FacturXDocument result = await CreateBuilder().BuildAsync(pivot);

        // Montants portés par le CII (BG-22) : BT-109 (HT), BT-110 (TVA), BT-112 (TTC).
        XDocument cii = XDocument.Parse(Encoding.UTF8.GetString(result.CrossIndustryInvoiceXml));
        decimal ciiNet = MonetaryValue(cii, "TaxBasisTotalAmount");
        decimal ciiTax = MonetaryValue(cii, "TaxTotalAmount");
        decimal ciiGross = MonetaryValue(cii, "GrandTotalAmount");

        // Montants RENDUS visuellement (PDF lisible).
        string pdfText = ExtractText(result.PdfBytes);
        string normalized = NormalizeAmounts(pdfText);

        AssertAmountRendered(normalized, ciiNet, pivot.CurrencyCode, "le total HT du PDF == BT-109 du CII");
        AssertAmountRendered(normalized, ciiTax, pivot.CurrencyCode, "le total TVA du PDF == BT-110 du CII");
        AssertAmountRendered(normalized, ciiGross, pivot.CurrencyCode, "le total TTC du PDF == BT-112 du CII");

        // Parité PAR GROUPE (BG-23) : chaque ventilation TVA document du CII (base BT-116 + TVA BT-117)
        // doit apparaître à l'identique dans le tableau de ventilation du PDF lisible (pas seulement les
        // totaux) — la dérivation visuelle est alignée sur BR-CO-17 du sérialiseur.
        foreach (XElement tradeTax in BreakdownTradeTaxes(cii))
        {
            decimal basis = ElementDecimal(tradeTax, "BasisAmount");
            decimal calculated = ElementDecimal(tradeTax, "CalculatedAmount");
            AssertAmountRendered(normalized, basis, pivot.CurrencyCode, "la base BG-23 du PDF == BT-116 du CII");
            AssertAmountRendered(normalized, calculated, pivot.CurrencyCode, "la TVA BG-23 du PDF == BT-117 du CII");
        }

        // Cohérence interne : BT-109 + BT-110 == BT-112 (les totaux du CII sont eux-mêmes cohérents).
        (ciiNet + ciiTax).Should().Be(ciiGross);
    }

    // Ventilations TVA AU NIVEAU DOCUMENT (BG-23) : les ApplicableTradeTax portant CalculatedAmount
    // (les ApplicableTradeTax de ligne n'en ont pas) — chacune porte BT-116 (base) et BT-117 (TVA).
    private static IEnumerable<XElement> BreakdownTradeTaxes(XDocument cii) =>
        cii.Descendants()
            .Where(e => e.Name.LocalName == "ApplicableTradeTax"
                        && e.Elements().Any(c => c.Name.LocalName == "CalculatedAmount"));

    private static decimal ElementDecimal(XElement parent, string localName) =>
        decimal.Parse(
            parent.Elements().First(e => e.Name.LocalName == localName).Value,
            CultureInfo.InvariantCulture);

    [Fact]
    public async Task BuildAsync_PropagatesSerializerBlock_WhenDocumentNotConformant()
    {
        // Pivot sans acheteur : le sérialiseur CII bloque (EN 16931 BR-07) — le builder propage le blocage
        // (fail-closed, jamais de Factur-X tronqué ; CLAUDE.md n°3).
        var pivot = new PivotDocumentDto(
            sourceDocumentKind: "INVOICE",
            number: "FAC-BLOCK",
            issueDate: new DateTime(2026, 1, 15),
            sourceReference: "SRC-BLOCK",
            supplier: new PivotPartyDto(
                "Vendeur",
                siren: "552100554",
                address: new PivotAddressDto("1 rue Test", null, "75001", "Paris", "FR")),
            totals: new PivotTotalsDto(100m, 20m, 120m),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: null,
            lines: new[]
            {
                new PivotLineDto(
                    "Ligne",
                    netAmount: 100m,
                    quantity: 1m,
                    unitPriceNet: 100m,
                    taxes: new[] { new PivotLineTaxDto(20m, 20m, VatCategory.S) }),
            });

        var act = async () => await CreateBuilder().BuildAsync(pivot);

        await act.Should().ThrowAsync<FacturXGenerationException>();
    }

    [Fact]
    public async Task BuildAsync_Throws_OnNullPivot()
    {
        var act = async () => await CreateBuilder().BuildAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // Lit un montant (xsd:decimal, format invariant « 0.00 ») du CII par nom local d'élément.
    private static decimal MonetaryValue(XDocument cii, string localName)
    {
        XElement element = cii.Descendants()
            .First(e => e.Name.LocalName == localName);
        return decimal.Parse(element.Value, CultureInfo.InvariantCulture);
    }

    private static string ExtractText(byte[] pdfBytes)
    {
        using PdfDocument document = PdfDocument.Open(pdfBytes);
        return string.Concat(document.GetPages().Select(p => p.Text));
    }

    // Neutralise tous les espaces (y compris les espaces insécables fr-FR) et convertit la virgule
    // décimale en point, pour comparer un montant rendu (« 1 234,56 EUR ») au format invariant.
    private static string NormalizeAmounts(string text) =>
        Regex.Replace(text, @"\s", string.Empty).Replace(",", ".", System.StringComparison.Ordinal);

    // Asserte qu'un montant (formaté « 0.00<devise> ») est rendu dans le texte normalisé, ANCRÉ à gauche
    // sur une frontière non-chiffre : « 20.00EUR » ne doit PAS être satisfait par « 120.00EUR » (sinon
    // faux-vert sur la parité, un total absent étant masqué par un montant plus grand qui le contient).
    private static void AssertAmountRendered(string normalizedText, decimal amount, string currencyCode, string because)
    {
        var token = amount.ToString("0.00", CultureInfo.InvariantCulture) + currencyCode;
        Regex.IsMatch(normalizedText, @"(?<!\d)" + Regex.Escape(token))
            .Should().BeTrue("{0} (montant {1} attendu dans le rendu)", because, token);
    }
}
