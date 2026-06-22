namespace Liakont.Agent.Adapters.DemoErpB;

using System;
using System.Collections.Generic;
using Liakont.Agent.Adapters.DemoErpB.Source;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Core.Extraction;
using Newtonsoft.Json;

/// <summary>
/// Transforme une facture BRUTE DemoErpB (<see cref="DemoErpBInvoice"/> + ses lignes) en document pivot
/// EN 16931. Respecte le contrat d'extraction (F01-F02 §4.2) : régimes bruts (R3), pas de classification
/// facture/avoir (<c>SourceDocumentKind</c> brut « I »/« C »), aucun calcul de montant. SEULE arithmétique
/// autorisée et OBLIGATOIRE : la conversion des <c>float</c> legacy en <c>decimal</c> arrondi au centime
/// half-up via <see cref="SourceAmounts"/> (ADR-0004 D3-7, CLAUDE.md n°1) ; l'original brut reste en SourceData.
/// L'émetteur et la nature d'opération ne sont PAS portés par l'agent : la plateforme les remplit à
/// l'ingestion depuis le profil tenant (ADR-0031 amendé) — l'agent n'extrait que la base source (F01-F02 §4.3).
/// </summary>
internal static class DemoErpBRowMapper
{
    /// <summary>Type de pièce source « facture » (invoice).</summary>
    internal const string KindInvoice = "I";

    /// <summary>Type de pièce source « avoir » (credit note).</summary>
    internal const string KindCreditNote = "C";

    /// <summary>Préfixe NAMESPACÉ de la référence source (anti-collision cross-agent — ADR-0031).</summary>
    internal const string SourceReferencePrefix = "demoerpb:";

    private const string CurrencyDefault = "EUR";

    /// <summary>Mappe une facture (ou un avoir) DemoErpB en document pivot.</summary>
    /// <param name="invoice">La facture source (avec ses lignes).</param>
    /// <returns>Le document pivot correspondant.</returns>
    public static PivotDocumentDto MapDocument(DemoErpBInvoice invoice)
    {
        if (invoice is null)
        {
            throw new ArgumentNullException(nameof(invoice));
        }

        string invoiceId = RequireField(invoice.InvoiceId, "InvoiceId", invoice.InvoiceId);
        string number = RequireField(invoice.InvoiceNo, "InvoiceNo", invoiceId);
        string kind = RequireField(invoice.Kind, "Kind", invoiceId);

        if (invoice.IssuedOn == default(DateTime))
        {
            throw new SourceSchemaException(
                $"Champ source obligatoire « IssuedOn » absent ou invalide (facture « {invoiceId} ») : "
                + "document bloqué, la date n'est jamais devinée (ADR-0004 D3-3). Vérifiez l'extraction des données source.");
        }

        var lines = new List<PivotLineDto>();
        foreach (DemoErpBItem item in invoice.Items)
        {
            lines.Add(MapItem(item, invoiceId));
        }

        PivotDocumentRefDto[] creditNoteRefs = MapCreditNoteRefs(invoice, kind);

        return new PivotDocumentDto(
            sourceDocumentKind: kind,
            number: number,
            issueDate: invoice.IssuedOn,
            sourceReference: SourceReferencePrefix + number,
            supplier: null,
            totals: new PivotTotalsDto(
                totalNet: SourceAmounts.RoundAmount(invoice.NetTotal, "NetTotal"),
                totalTax: SourceAmounts.RoundAmount(invoice.VatTotal, "VatTotal"),
                totalGross: SourceAmounts.RoundAmount(invoice.GrossTotal, "GrossTotal"),
                sourceTotalGross: SourceAmounts.RoundAmount(invoice.GrossTotal, "GrossTotal")),
            operationCategory: null,
            currencyCode: string.IsNullOrWhiteSpace(invoice.Currency) ? CurrencyDefault : invoice.Currency!.Trim(),
            customer: MapCustomer(invoice),
            lines: lines,
            creditNoteRefs: creditNoteRefs,
            payments: null,
            documentCharges: null,
            invoicer: null,
            payee: null,
            isSelfBilled: false,
            prepaidAmount: null,
            sourceData: BuildSourceData(invoice),
            paymentDueDate: null);
    }

    private static PivotLineDto MapItem(DemoErpBItem item, string invoiceId)
    {
        string description = RequireField(item.Label, "Label", invoiceId);

        IReadOnlyList<string> regimeCodes = string.IsNullOrWhiteSpace(item.VatRegime)
            ? Array.Empty<string>()
            : new[] { item.VatRegime!.Trim() };

        var taxes = new[]
        {
            new PivotLineTaxDto(
                taxAmount: SourceAmounts.RoundAmount(item.VatAmount, "VatAmount"),
                rate: item.VatRate.HasValue ? SourceAmounts.ToDecimal(item.VatRate.Value, "VatRate") : (decimal?)null,
                categoryCode: null,
                vatexCode: null),
        };

        return new PivotLineDto(
            description: description,
            netAmount: SourceAmounts.RoundAmount(item.NetAmount, "NetAmount"),
            quantity: SourceAmounts.ToDecimal(item.Qty, "Qty"),
            unitPriceNet: item.UnitPrice.HasValue ? SourceAmounts.RoundAmount(item.UnitPrice.Value, "UnitPrice") : (decimal?)null,
            sourceRegimeCodes: regimeCodes,
            taxes: taxes,
            sourceLineRef: item.LineNumber,
            sourceData: null);
    }

    private static PivotPartyDto? MapCustomer(DemoErpBInvoice invoice)
    {
        // B2C anonyme possible (e-reporting B2C non nominatif) — pas de tiers destinataire (F01-F02 §6).
        if (string.IsNullOrWhiteSpace(invoice.BuyerName))
        {
            return null;
        }

        // IsCompanyHint = transcription BRUTE du champ source « BuyerIsCompany » — aucune heuristique (VAL05).
        return new PivotPartyDto(
            name: invoice.BuyerName!,
            siren: invoice.BuyerSiren,
            isCompanyHint: invoice.BuyerIsCompany);
    }

    private static PivotDocumentRefDto[] MapCreditNoteRefs(DemoErpBInvoice invoice, string kind)
    {
        if (!string.Equals(kind, KindCreditNote, StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<PivotDocumentRefDto>();
        }

        if (string.IsNullOrWhiteSpace(invoice.OriginInvoiceNo) || invoice.OriginIssuedOn is null)
        {
            throw new SourceSchemaException(
                $"Avoir « {invoice.InvoiceNo} » sans facture d'origine résoluble (origine « {invoice.OriginInvoiceNo} ») "
                + "ou sans date d'origine : document bloqué, l'origine n'est jamais devinée (ADR-0004 D3-3).");
        }

        return new[]
        {
            new PivotDocumentRefDto(
                number: invoice.OriginInvoiceNo!,
                issueDate: invoice.OriginIssuedOn.Value,
                sourceReference: SourceReferencePrefix + invoice.OriginInvoiceNo),
        };
    }

    private static string RequireField(string? value, string fieldName, string? invoiceId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new SourceSchemaException(
                $"Champ source obligatoire « {fieldName} » absent (facture « {invoiceId} ») : schéma incompatible. Vérifiez l'extraction.");
        }

        return value!;
    }

    private static string BuildSourceData(DemoErpBInvoice invoice) =>
        JsonConvert.SerializeObject(new SourceDataDocument
        {
            InvoiceId = invoice.InvoiceId,
            NetTotalBrut = invoice.NetTotal,
            VatTotalBrut = invoice.VatTotal,
            GrossTotalBrut = invoice.GrossTotal,
        });

    // Vue déterministe des montants source ORIGINAUX (flottants non arrondis), sérialisée pour la
    // traçabilité (F01-F02 §3.7 règle 1) — empreinte stable.
    private sealed class SourceDataDocument
    {
        [JsonProperty("invoice_id", NullValueHandling = NullValueHandling.Include)]
        public string? InvoiceId { get; set; }

        [JsonProperty("net_total_brut")]
        public double NetTotalBrut { get; set; }

        [JsonProperty("vat_total_brut")]
        public double VatTotalBrut { get; set; }

        [JsonProperty("gross_total_brut")]
        public double GrossTotalBrut { get; set; }
    }
}
