namespace Liakont.Modules.Pipeline.Infrastructure.Send;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Documents.Contracts.DTOs;

/// <summary>
/// Compose la requête d'archivage WORM (TRK05) d'un document émis à partir du pivot relu du staging
/// (PIP00) et de la réponse brute de la Plateforme Agréée. AUCUNE règle fiscale n'est inventée ici : le
/// rendu lisible reprend EXACTEMENT les montants (en <see cref="decimal"/>, CLAUDE.md n°1), catégories et
/// taux déjà portés par le pivot enrichi (issus de la table validée du tenant au CHECK). La facture PA et
/// le bordereau source sont ABSENTS à l'émission — leurs motifs d'absence sont explicites (jamais une
/// absence silencieuse) : la synchronisation (SYNC, PIP01d) ajoute la facture PA en addendum si la PA
/// déclare la capacité de récupération.
/// </summary>
internal static class SendArchiveComposer
{
    /// <summary>Motif d'absence de la facture PA à l'émission (récupérée ultérieurement par SYNC, PIP01d).</summary>
    internal const string PaInvoiceAbsenceReason =
        "Facture légale non récupérée à l'émission : la synchronisation (SYNC, PIP01d) l'ajoute en addendum " +
        "si la Plateforme Agréée déclare la capacité de récupération de document.";

    /// <summary>Motif d'absence du bordereau source à l'émission (non fourni par l'adaptateur à ce stade).</summary>
    internal const string SourceDocumentAbsenceReason =
        "Bordereau du logiciel source non fourni à l'émission par l'adaptateur d'extraction.";

    /// <summary>
    /// Construit la requête d'archivage d'un document émis. <paramref name="payloadJson"/> est le pivot
    /// canonique EXACT relu du staging (la représentation faisant foi de ce qui a été transmis) ;
    /// <paramref name="paResponseJson"/> est la réponse brute de la PA (preuve de transmission).
    /// </summary>
    public static ArchivePackageRequest Compose(
        DocumentDto document,
        PivotDocumentDto pivot,
        string payloadJson,
        string paResponseJson,
        string mappingTraceJson)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pivot);

        return new ArchivePackageRequest
        {
            DocumentId = document.Id,
            DocumentNumber = document.DocumentNumber,
            IssueDate = document.IssueDate,
            PayloadJson = payloadJson,
            PaResponseJson = paResponseJson,
            Readable = BuildReadable(document, pivot),
            MappingTraceJson = mappingTraceJson,
            PaInvoice = null,
            PaInvoiceAbsenceReason = PaInvoiceAbsenceReason,
            SourceDocument = null,
            SourceDocumentAbsenceReason = SourceDocumentAbsenceReason,
        };
    }

    private static ArchiveReadableDocument BuildReadable(DocumentDto document, PivotDocumentDto pivot)
    {
        var lines = pivot.Lines
            .Select(line => new ArchiveReadableLine(
                line.Description,
                line.Quantity,
                line.UnitPriceNet,
                line.NetAmount,
                RateLabel(PrimaryRate(line))))
            .ToList();

        return new ArchiveReadableDocument(
            DocumentNumber: document.DocumentNumber,
            DocumentTypeLabel: DocumentTypeLabel(pivot),
            IssueDate: document.IssueDate,
            CurrencyCode: pivot.CurrencyCode,
            SellerName: pivot.Supplier.Name,
            SellerSiren: pivot.Supplier.Siren,
            BuyerName: pivot.Customer?.Name,
            Lines: lines,
            VatBreakdown: BuildVatBreakdown(pivot),
            TotalNet: pivot.Totals.TotalNet,
            TotalTax: pivot.Totals.TotalTax,
            TotalGross: pivot.Totals.TotalGross);
    }

    /// <summary>
    /// Ventilation TVA du rendu lisible : agrège, PAR taux, la base imposable (somme des montants nets de
    /// ligne) et la TVA (somme des montants de taxe). Montants en <see cref="decimal"/>, aucun float.
    /// Reprend les taux portés par le pivot — aucun taux n'est deviné.
    /// </summary>
    private static List<ArchiveVatBreakdownLine> BuildVatBreakdown(PivotDocumentDto pivot)
    {
        var byRate = new Dictionary<string, (decimal Base, decimal Tax)>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var line in pivot.Lines)
        {
            var label = RateLabel(PrimaryRate(line));
            decimal lineTax = line.Taxes.Sum(tax => tax.TaxAmount);

            if (byRate.TryGetValue(label, out var current))
            {
                byRate[label] = (current.Base + line.NetAmount, current.Tax + lineTax);
            }
            else
            {
                byRate[label] = (line.NetAmount, lineTax);
                order.Add(label);
            }
        }

        return order
            .Select(label => new ArchiveVatBreakdownLine(label, byRate[label].Base, byRate[label].Tax))
            .ToList();
    }

    /// <summary>Taux de la première taxe de la ligne (forme nominale : une taxe par ligne), ou <c>null</c>.</summary>
    private static decimal? PrimaryRate(PivotLineDto line) =>
        line.Taxes.Count > 0 ? line.Taxes[0].Rate : null;

    private static string RateLabel(decimal? rate) =>
        rate.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"{rate.Value} %")
            : "Taux non précisé";

    /// <summary>
    /// Libellé d'affichage du type de document pour le rendu lisible (NON fiscal) : un document portant des
    /// références d'avoir est un « Avoir », sinon une « Facture ». La classification fiscale réelle reste
    /// l'affaire de la validation (le pivot ne classe pas — <see cref="PivotDocumentDto.SourceDocumentKind"/>
    /// est brut).
    /// </summary>
    private static string DocumentTypeLabel(PivotDocumentDto pivot) =>
        pivot.CreditNoteRefs.Count > 0 ? "Avoir" : "Facture";
}
