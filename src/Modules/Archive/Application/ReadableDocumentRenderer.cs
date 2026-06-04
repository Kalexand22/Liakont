namespace Liakont.Modules.Archive.Application;

using System.Globalization;
using System.Text;
using Liakont.Modules.Archive.Contracts;

/// <summary>
/// Rend un document lisible AUTONOME en HTML (TRK05 §2 : <c>document-lisible.html</c>), exigence de
/// lisibilité de l'art. 289 V CGI — le document doit rester lisible « jusqu'à la fin de la conservation »,
/// sans le logiciel source ni la plateforme. Le rendu est un HTML statique, sans ressource externe (CSS
/// en ligne), en français, avec en-tête, lignes, ventilation de TVA et totaux. Aucune logique fiscale :
/// les libellés de type et de taux sont fournis par l'appelant (jamais inventés ici).
/// </summary>
public static class ReadableDocumentRenderer
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Produit le HTML autonome du document, encodé en UTF-8.</summary>
    public static byte[] Render(ArchiveReadableDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html lang=\"fr\">\n<head>\n");
        sb.Append("<meta charset=\"utf-8\">\n");
        sb.Append("<title>").Append(Encode(document.DocumentTypeLabel)).Append(' ').Append(Encode(document.DocumentNumber)).Append("</title>\n");
        sb.Append("<style>\n");
        sb.Append("body{font-family:Arial,Helvetica,sans-serif;color:#1a1a1a;margin:2em;}\n");
        sb.Append("h1{font-size:1.4em;}table{border-collapse:collapse;width:100%;margin-top:1em;}\n");
        sb.Append("th,td{border:1px solid #999;padding:4px 8px;text-align:left;}\n");
        sb.Append("td.amount,th.amount{text-align:right;}tfoot td{font-weight:bold;}\n");
        sb.Append(".meta{margin:0.2em 0;}\n");
        sb.Append("</style>\n</head>\n<body>\n");

        sb.Append("<h1>").Append(Encode(document.DocumentTypeLabel)).Append(" n° ").Append(Encode(document.DocumentNumber)).Append("</h1>\n");
        sb.Append("<p class=\"meta\">Date d'émission : ").Append(Encode(FormatDate(document.IssueDate))).Append("</p>\n");
        sb.Append("<p class=\"meta\">Émetteur : ").Append(Encode(document.SellerName));
        if (!string.IsNullOrWhiteSpace(document.SellerSiren))
        {
            sb.Append(" (SIREN ").Append(Encode(document.SellerSiren)).Append(')');
        }

        sb.Append("</p>\n");
        sb.Append("<p class=\"meta\">Destinataire : ").Append(Encode(document.BuyerName ?? "Non identifié (B2C)")).Append("</p>\n");

        RenderLines(sb, document);
        RenderVatBreakdown(sb, document);
        RenderTotals(sb, document);

        sb.Append("</body>\n</html>\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void RenderLines(StringBuilder sb, ArchiveReadableDocument document)
    {
        sb.Append("<h2>Lignes</h2>\n<table>\n<thead><tr>");
        sb.Append("<th>Désignation</th><th class=\"amount\">Quantité</th><th class=\"amount\">P.U. HT</th>");
        sb.Append("<th class=\"amount\">Montant HT</th><th>TVA</th></tr></thead>\n<tbody>\n");
        foreach (ArchiveReadableLine line in document.Lines)
        {
            sb.Append("<tr><td>").Append(Encode(line.Designation)).Append("</td>");
            sb.Append("<td class=\"amount\">").Append(Encode(FormatNullable(line.Quantity))).Append("</td>");
            sb.Append("<td class=\"amount\">").Append(Encode(FormatNullableMoney(line.UnitPrice, document.CurrencyCode))).Append("</td>");
            sb.Append("<td class=\"amount\">").Append(Encode(FormatMoney(line.NetAmount, document.CurrencyCode))).Append("</td>");
            sb.Append("<td>").Append(Encode(line.VatRateLabel ?? "—")).Append("</td></tr>\n");
        }

        sb.Append("</tbody>\n</table>\n");
    }

    private static void RenderVatBreakdown(StringBuilder sb, ArchiveReadableDocument document)
    {
        sb.Append("<h2>Ventilation de TVA</h2>\n<table>\n<thead><tr>");
        sb.Append("<th>Taux / nature</th><th class=\"amount\">Base HT</th><th class=\"amount\">TVA</th></tr></thead>\n<tbody>\n");
        foreach (ArchiveVatBreakdownLine vat in document.VatBreakdown)
        {
            sb.Append("<tr><td>").Append(Encode(vat.VatRateLabel)).Append("</td>");
            sb.Append("<td class=\"amount\">").Append(Encode(FormatMoney(vat.TaxableBase, document.CurrencyCode))).Append("</td>");
            sb.Append("<td class=\"amount\">").Append(Encode(FormatMoney(vat.TaxAmount, document.CurrencyCode))).Append("</td></tr>\n");
        }

        sb.Append("</tbody>\n</table>\n");
    }

    private static void RenderTotals(StringBuilder sb, ArchiveReadableDocument document)
    {
        sb.Append("<h2>Totaux</h2>\n<table>\n<tbody>\n");
        sb.Append("<tr><td>Total HT</td><td class=\"amount\">").Append(Encode(FormatMoney(document.TotalNet, document.CurrencyCode))).Append("</td></tr>\n");
        sb.Append("<tr><td>Total TVA</td><td class=\"amount\">").Append(Encode(FormatMoney(document.TotalTax, document.CurrencyCode))).Append("</td></tr>\n");
        sb.Append("<tr><td>Total TTC</td><td class=\"amount\">").Append(Encode(FormatMoney(document.TotalGross, document.CurrencyCode))).Append("</td></tr>\n");
        sb.Append("</tbody>\n</table>\n");
    }

    // Échappement HTML MINIMAL : seuls les 5 caractères significatifs sont neutralisés (anti-injection) ;
    // les lettres accentuées restent en UTF-8 (charset déclaré) pour un rendu lisible et stable dans 10 ans.
    private static string Encode(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);

    private static string FormatDate(DateOnly date) => date.ToString("dd/MM/yyyy", Fr);

    private static string FormatMoney(decimal amount, string currencyCode) =>
        amount.ToString("N2", Fr) + " " + currencyCode;

    private static string FormatNullableMoney(decimal? amount, string currencyCode) =>
        amount.HasValue ? FormatMoney(amount.Value, currencyCode) : "—";

    private static string FormatNullable(decimal? value) =>
        value.HasValue ? value.Value.ToString("0.###", Fr) : "—";
}
