namespace Liakont.Modules.Reconciliation.Tests.Unit;

using System.Text;
using FluentAssertions;
using Liakont.Modules.Reconciliation.Infrastructure;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

public sealed class PdfPigTextExtractorTests
{
    [Fact]
    public void TryExtractText_FromGeneratedPdf_ReturnsTextContainingDocumentNumber()
    {
        byte[] pdfBytes = BuildPdfWithText("FAC-2026-0042");
        var extractor = new PdfPigTextExtractor();

        string? text = extractor.TryExtractText(pdfBytes);

        text.Should().NotBeNull();
        text!.Should().Contain("FAC-2026-0042");
    }

    [Fact]
    public void TryExtractText_NonPdfContent_ReturnsNull()
    {
        var extractor = new PdfPigTextExtractor();

        // Contenu non-PDF : ne lève jamais (orphelin), renvoie null (INV-RECONCILIATION-009).
        extractor.TryExtractText(Encoding.UTF8.GetBytes("ceci n'est pas un PDF")).Should().BeNull();
    }

    [Fact]
    public void TryExtractText_EmptyContent_ReturnsNull()
    {
        var extractor = new PdfPigTextExtractor();

        extractor.TryExtractText([]).Should().BeNull();
    }

    private static byte[] BuildPdfWithText(string text)
    {
        var builder = new PdfDocumentBuilder();
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);
        PdfPageBuilder page = builder.AddPage(PageSize.A4);
        page.AddText(text, 12, new PdfPoint(25, 700), font);
        return builder.Build();
    }
}
