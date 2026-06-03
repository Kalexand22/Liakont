namespace Stratum.Common.UI.Services;

using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

/// <summary>
/// Internal helper that generates a PDF table from column definitions and row data.
/// Used by StratumDataGrid for built-in PDF export.
/// </summary>
internal static class PdfExportHelper
{
    private const int LandscapeColumnThreshold = 5;

    /// <summary>
    /// Generates a PDF document as a byte array.
    /// </summary>
    /// <param name="title">Page title displayed at the top of each page.</param>
    /// <param name="columns">Column definitions (property name + display title).</param>
    /// <param name="rows">Pre-extracted row data: each row is a list of string cell values matching the column order.</param>
    /// <param name="filterSummary">Optional summary of currently applied filters.</param>
    /// <returns>PDF file bytes.</returns>
    public static byte[] Generate(
        string title,
        IReadOnlyList<(string Property, string Title)> columns,
        IReadOnlyList<IReadOnlyList<string>> rows,
        string? filterSummary = null)
    {
        var useFullLandscape = columns.Count > LandscapeColumnThreshold;
        var pageSize = useFullLandscape ? PageSizes.A4.Landscape() : PageSizes.A4;
        var exportDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(pageSize);
                page.MarginHorizontal(30);
                page.MarginVertical(25);
                page.DefaultTextStyle(x => x.FontSize(9));

                // ── Header ──
                page.Header().Column(header =>
                {
                    header.Item().Text(title).FontSize(14).SemiBold();

                    if (!string.IsNullOrWhiteSpace(filterSummary))
                    {
                        header.Item().Text(filterSummary).FontSize(8).FontColor(Colors.Grey.Medium);
                    }

                    header.Item().PaddingBottom(8).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                });

                // ── Content ──
                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(def =>
                    {
                        for (var i = 0; i < columns.Count; i++)
                        {
                            def.RelativeColumn();
                        }
                    });

                    // Column headers
                    foreach (var col in columns)
                    {
                        table.Cell()
                            .BorderBottom(1).BorderColor(Colors.Grey.Lighten1)
                            .Padding(4)
                            .Text(col.Title).SemiBold().FontSize(9);
                    }

                    // Data rows
                    var isAlternate = false;
                    foreach (var row in rows)
                    {
                        var bgColor = isAlternate ? Colors.Grey.Lighten4 : Colors.White;

                        for (var c = 0; c < columns.Count; c++)
                        {
                            var cellValue = c < row.Count ? row[c] : string.Empty;
                            table.Cell()
                                .Background(bgColor)
                                .Padding(4)
                                .Text(cellValue).FontSize(9);
                        }

                        isAlternate = !isAlternate;
                    }
                });

                // ── Footer ──
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span(exportDate).FontSize(7).FontColor(Colors.Grey.Medium);
                    text.Span("  —  Page ").FontSize(7).FontColor(Colors.Grey.Medium);
                    text.CurrentPageNumber().FontSize(7);
                    text.Span(" / ").FontSize(7).FontColor(Colors.Grey.Medium);
                    text.TotalPages().FontSize(7);
                });
            });
        });

        return document.GeneratePdf();
    }
}
