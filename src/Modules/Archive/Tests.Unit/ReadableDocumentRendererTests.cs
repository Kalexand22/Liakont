namespace Liakont.Modules.Archive.Tests.Unit;

using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Contracts;
using Xunit;

public sealed class ReadableDocumentRendererTests
{
    [Fact]
    public void Render_IncludesHeaderLinesAndTotals()
    {
        string html = Encoding.UTF8.GetString(ReadableDocumentRenderer.Render(ArchiveTestData.Readable()));

        html.Should().StartWith("<!DOCTYPE html>");
        html.Should().Contain("F-2026-001");
        html.Should().Contain("Prestation de service");
        html.Should().Contain("ACME Ventes SARL");
        html.Should().Contain("Total TTC");

        // Montant en euros présent dans le rendu.
        html.Should().Contain("EUR");
        html.Should().Contain("12/05/2026");
    }

    [Fact]
    public void Render_EncodesHtmlToPreventInjection()
    {
        var doc = new ArchiveReadableDocument(
            DocumentNumber: "F-1",
            DocumentTypeLabel: "Facture",
            IssueDate: new System.DateOnly(2026, 1, 1),
            CurrencyCode: "EUR",
            SellerName: "Vendeur",
            SellerSiren: null,
            BuyerName: null,
            Lines: new List<ArchiveReadableLine> { new("<script>alert(1)</script>", null, null, 10m, null) },
            VatBreakdown: new List<ArchiveVatBreakdownLine>(),
            TotalNet: 10m,
            TotalTax: 0m,
            TotalGross: 10m);

        string html = Encoding.UTF8.GetString(ReadableDocumentRenderer.Render(doc));

        html.Should().NotContain("<script>alert(1)</script>");
        html.Should().Contain("&lt;script&gt;");
        html.Should().Contain("Non identifié (B2C)");
    }
}
