namespace Stratum.Common.UI.Tests.Unit;

using FluentAssertions;
using QuestPDF.Infrastructure;
using Stratum.Common.UI.Services;
using Xunit;

public sealed class PdfExportHelperTests
{
    private static readonly bool LicenseConfigured = ConfigureLicense();

    private static readonly List<(string Property, string Title)> ThreeColumns =
    [
        ("Name", "Nom"),
        ("Category", "Catégorie"),
        ("Price", "Prix"),
    ];

    private static readonly List<(string Property, string Title)> SixColumns =
    [
        ("Name", "Nom"),
        ("Category", "Catégorie"),
        ("Price", "Prix"),
        ("Currency", "Devise"),
        ("Stock", "Stock"),
        ("Active", "Actif"),
    ];

    [Fact]
    public void ShouldReturnValidPdfBytesForNominalCase()
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new List<string> { "Widget", "Hardware", "19.99" },
            new List<string> { "Gadget", "Electronics", "49.99" },
        };

        var result = PdfExportHelper.Generate("Products", ThreeColumns, rows);

        result.Should().NotBeEmpty();

        // PDF magic bytes: %PDF
        result[0].Should().Be(0x25);
        result[1].Should().Be(0x50);
        result[2].Should().Be(0x44);
        result[3].Should().Be(0x46);
    }

    [Fact]
    public void ShouldReturnValidPdfForEmptyRows()
    {
        var rows = new List<IReadOnlyList<string>>();

        var result = PdfExportHelper.Generate("Empty", ThreeColumns, rows);

        result.Should().NotBeEmpty();
        result[0].Should().Be(0x25);
    }

    [Fact]
    public void ShouldHandleRowShorterThanColumns()
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new List<string> { "Widget" },
        };

        var act = () => PdfExportHelper.Generate("Test", ThreeColumns, rows);

        act.Should().NotThrow();
        var result = act();
        result.Should().NotBeEmpty();
    }

    [Fact]
    public void ShouldProduceValidPdfForWideGridWithLandscape()
    {
        var wideRows = new List<IReadOnlyList<string>>
        {
            new List<string> { "A", "B", "C", "D", "E", "F" },
        };

        var result = PdfExportHelper.Generate("Wide", SixColumns, wideRows);

        result.Should().NotBeEmpty();
        result[0].Should().Be(0x25);
    }

    [Fact]
    public void ShouldIncludeFilterSummaryWithoutError()
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new List<string> { "Widget", "Hardware", "19.99" },
        };

        var result = PdfExportHelper.Generate("Products", ThreeColumns, rows, "Filtre: Catégorie = Hardware");

        result.Should().NotBeEmpty();
        result[0].Should().Be(0x25);
    }

    private static bool ConfigureLicense()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return true;
    }
}
