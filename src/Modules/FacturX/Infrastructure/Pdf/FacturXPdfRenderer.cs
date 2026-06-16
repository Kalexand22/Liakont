namespace Liakont.Modules.FacturX.Infrastructure.Pdf;

using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

/// <summary>
/// Rend la couche VISUELLE lisible du Factur-X (F16 §5) en PDF/A-3b avec QuestPDF (ADR-0023 §1 ;
/// QuestPDF CONFINÉE à cette couche, INV-FX-1). La conformité PDF/A-3b est activée par
/// <c>DocumentSettings.PdfA = true</c> : dans QuestPDF 2025.7.4 c'est l'unique levier (l'enum
/// <c>PDFA_Conformance.PDFA_3B</c> évoqué par la doc « latest »/ADR n'existe pas dans cette version —
/// <c>PdfA = true</c> ⇒ PDF/A-3b, fontes embarquées + OutputIntent sRGB + UUID + XMP de base). Le
/// <c>factur-x.xml</c> et le bloc XMP <c>fx:</c> sont ajoutés ENSUITE par <see cref="FacturXBuilder"/>
/// (passe <c>DocumentOperation</c>). Rendu déterministe du modèle (donc du pivot seul, INV-FX-4) ;
/// montants en <see cref="decimal"/> formatés culture fr-FR, aucune règle fiscale (CLAUDE.md n°1/2).
/// </summary>
internal static class FacturXPdfRenderer
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Produit le PDF/A-3b visuel (sans pièce jointe ni XMP fx:, ajoutés par le builder).</summary>
    public static byte[] Render(FacturXReadableModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(30);
                page.MarginVertical(25);
                page.DefaultTextStyle(x => x.FontSize(9));

                ComposeHeader(page.Header(), model);
                ComposeContent(page.Content(), model);
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Page ").FontSize(7).FontColor(Colors.Grey.Medium);
                    text.CurrentPageNumber().FontSize(7);
                    text.Span(" / ").FontSize(7).FontColor(Colors.Grey.Medium);
                    text.TotalPages().FontSize(7);
                });
            });
        })
        .WithSettings(new DocumentSettings { PdfA = true });

        return document.GeneratePdf();
    }

    private static void ComposeHeader(IContainer header, FacturXReadableModel model)
    {
        header.Column(column =>
        {
            column.Item().Text($"{model.DocumentTypeLabel} n° {model.DocumentNumber}").FontSize(15).SemiBold();
            column.Item().Text($"Date d'émission : {FormatDate(model.IssueDate)}").FontSize(9);
            if (model.DueDate is { } due)
            {
                column.Item().Text($"Échéance de paiement : {FormatDate(due)}").FontSize(9);
            }

            column.Item().PaddingBottom(8).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
        });
    }

    private static void ComposeContent(IContainer content, FacturXReadableModel model)
    {
        content.Column(column =>
        {
            column.Spacing(10);
            ComposeParties(column.Item(), model);
            ComposeLines(column.Item(), model);
            ComposeVatBreakdown(column.Item(), model);
            ComposeTotals(column.Item(), model);
        });
    }

    private static void ComposeParties(IContainer container, FacturXReadableModel model)
    {
        container.Column(column =>
        {
            var seller = model.SellerName;
            if (!string.IsNullOrWhiteSpace(model.SellerSiren))
            {
                seller += $" (SIREN {model.SellerSiren})";
            }

            if (!string.IsNullOrWhiteSpace(model.SellerVatNumber))
            {
                seller += $" — TVA {model.SellerVatNumber}";
            }

            column.Item().Text($"Émetteur : {seller}").FontSize(9);
            column.Item().Text($"Destinataire : {model.BuyerName ?? "Non identifié (B2C)"}").FontSize(9);
        });
    }

    private static void ComposeLines(IContainer container, FacturXReadableModel model)
    {
        container.Column(column =>
        {
            column.Item().Text("Lignes").FontSize(11).SemiBold();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(def =>
                {
                    def.RelativeColumn(4);
                    def.RelativeColumn();
                    def.RelativeColumn();
                    def.RelativeColumn();
                    def.RelativeColumn(2);
                });

                HeaderCell(table, "Désignation");
                HeaderCell(table, "Quantité", alignRight: true);
                HeaderCell(table, "P.U. HT", alignRight: true);
                HeaderCell(table, "Montant HT", alignRight: true);
                HeaderCell(table, "TVA");

                foreach (var line in model.Lines)
                {
                    BodyCell(table, line.Designation);
                    BodyCell(table, FormatQuantity(line.Quantity), alignRight: true);
                    BodyCell(table, FormatNullableMoney(line.UnitPrice, model.CurrencyCode), alignRight: true);
                    BodyCell(table, FormatMoney(line.NetAmount, model.CurrencyCode), alignRight: true);
                    BodyCell(table, line.VatRateLabel);
                }
            });
        });
    }

    private static void ComposeVatBreakdown(IContainer container, FacturXReadableModel model)
    {
        container.Column(column =>
        {
            column.Item().Text("Ventilation de TVA").FontSize(11).SemiBold();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(def =>
                {
                    def.RelativeColumn(3);
                    def.RelativeColumn();
                    def.RelativeColumn();
                });

                HeaderCell(table, "Taux / nature");
                HeaderCell(table, "Base HT", alignRight: true);
                HeaderCell(table, "TVA", alignRight: true);

                foreach (var vat in model.VatBreakdown)
                {
                    BodyCell(table, vat.VatRateLabel);
                    BodyCell(table, FormatMoney(vat.TaxableBase, model.CurrencyCode), alignRight: true);
                    BodyCell(table, FormatMoney(vat.TaxAmount, model.CurrencyCode), alignRight: true);
                }
            });
        });
    }

    private static void ComposeTotals(IContainer container, FacturXReadableModel model)
    {
        container.Column(column =>
        {
            column.Item().Text("Totaux").FontSize(11).SemiBold();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(def =>
                {
                    def.RelativeColumn(3);
                    def.RelativeColumn();
                });

                TotalRow(table, "Total HT", FormatMoney(model.TotalNet, model.CurrencyCode));
                TotalRow(table, "Total TVA", FormatMoney(model.TotalTax, model.CurrencyCode));
                TotalRow(table, "Total TTC", FormatMoney(model.TotalGross, model.CurrencyCode));
                if (model.Prepaid is { } prepaid && prepaid != 0m)
                {
                    TotalRow(table, "Acompte versé", FormatMoney(prepaid, model.CurrencyCode));
                }

                TotalRow(table, "Net à payer", FormatMoney(model.DuePayable, model.CurrencyCode), strong: true);
            });
        });
    }

    private static void HeaderCell(TableDescriptor table, string text, bool alignRight = false)
    {
        var cell = table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).Padding(4);
        if (alignRight)
        {
            cell = cell.AlignRight();
        }

        cell.Text(text).SemiBold().FontSize(9);
    }

    private static void BodyCell(TableDescriptor table, string text, bool alignRight = false)
    {
        var cell = table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(4);
        if (alignRight)
        {
            cell = cell.AlignRight();
        }

        cell.Text(text).FontSize(9);
    }

    private static void TotalRow(TableDescriptor table, string label, string amount, bool strong = false)
    {
        var labelCell = table.Cell().Padding(4);
        var amountCell = table.Cell().AlignRight().Padding(4);
        if (strong)
        {
            labelCell.Text(label).SemiBold().FontSize(10);
            amountCell.Text(amount).SemiBold().FontSize(10);
        }
        else
        {
            labelCell.Text(label).FontSize(9);
            amountCell.Text(amount).FontSize(9);
        }
    }

    private static string FormatDate(DateOnly date) => date.ToString("dd/MM/yyyy", Fr);

    private static string FormatMoney(decimal amount, string currencyCode) =>
        amount.ToString("N2", Fr) + " " + currencyCode;

    private static string FormatNullableMoney(decimal? amount, string currencyCode) =>
        amount.HasValue ? FormatMoney(amount.Value, currencyCode) : "—";

    private static string FormatQuantity(decimal value) => value.ToString("0.###", Fr);
}
