namespace Liakont.Modules.FacturX.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.FacturX.Contracts;
using Xunit;

/// <summary>
/// Comportement de l'artefact <see cref="FacturXDocument"/> : un artefact de conformité ne se
/// construit jamais incomplet (CLAUDE.md n°3). FX02 livre l'ossature ; la génération réelle (FX04)
/// produit l'artefact à partir du pivot.
/// </summary>
public sealed class FacturXDocumentTests
{
    [Fact]
    public void Constructor_WithValidArguments_ExposesThemUnchanged()
    {
        var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var xml = new byte[] { 0x3C, 0x3F, 0x78, 0x6D, 0x6C };

        var artefact = new FacturXDocument(pdf, "FAC-2026-0001.pdf", xml);

        artefact.PdfBytes.Should().BeSameAs(pdf);
        artefact.CrossIndustryInvoiceXml.Should().BeSameAs(xml);
        artefact.FileName.Should().Be("FAC-2026-0001.pdf");
    }

    [Fact]
    public void Constructor_NullPdfBytes_Throws()
    {
        var act = () => new FacturXDocument(null!, "f.pdf", []);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullXml_Throws()
    {
        var act = () => new FacturXDocument([], "f.pdf", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankFileName_Throws(string fileName)
    {
        var act = () => new FacturXDocument([], fileName, []);
        act.Should().Throw<ArgumentException>();
    }
}
