namespace Liakont.Modules.Archive.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Text;
using Liakont.Modules.Archive.Contracts;

/// <summary>Fabriques de données de test pour le module Archive (exemples fictifs, CLAUDE.md n°7).</summary>
internal static class ArchiveTestData
{
    public const string Tenant = "acme";

    public static ArchiveReadableDocument Readable(string number = "F-2026-001") => new(
        DocumentNumber: number,
        DocumentTypeLabel: "Facture",
        IssueDate: new DateOnly(2026, 5, 12),
        CurrencyCode: "EUR",
        SellerName: "ACME Ventes SARL",
        SellerSiren: "123456789",
        BuyerName: "Client Démo",
        Lines: new List<ArchiveReadableLine>
        {
            new("Prestation de service", 1m, 1000.00m, 1000.00m, "20 %"),
        },
        VatBreakdown: new List<ArchiveVatBreakdownLine>
        {
            new("20 %", 1000.00m, 200.00m),
        },
        TotalNet: 1000.00m,
        TotalTax: 200.00m,
        TotalGross: 1200.00m);

    public static ArchivePackageRequest PackageRequest(
        string number = "F-2026-001",
        Guid? documentId = null,
        bool withPaInvoice = true,
        bool withSourceDocument = true) => new()
    {
        DocumentId = documentId ?? Guid.NewGuid(),
        DocumentNumber = number,
        IssueDate = new DateOnly(2026, 5, 12),
        PayloadJson = """{"number":"F-2026-001","total":1200.00}""",
        PaResponseJson = """{"paDocumentId":"PA-42","ledgerId":"DGFIP-7"}""",
        Readable = Readable(number),
        MappingTraceJson = """{"rule":"R-20","version":3}""",
        PaInvoice = withPaInvoice ? new ArchiveAttachment("facture-pa.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF-facture")) : null,
        PaInvoiceAbsenceReason = withPaInvoice ? null : "La PA ne déclare pas SupportsDocumentRetrieval.",
        SourceDocument = withSourceDocument ? new ArchiveAttachment("bordereau-source.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF-bordereau")) : null,
        SourceDocumentAbsenceReason = withSourceDocument ? null : "L'adaptateur ne déclare pas ProvidesSourceDocuments.",
    };

    public static ArchiveAddendumRequest AddendumRequest(
        Guid documentId,
        string number = "F-2026-001",
        string fileName = "tax-report.xml",
        string kind = "tax-report") => new()
    {
        DocumentId = documentId,
        DocumentNumber = number,
        IssueDate = new DateOnly(2026, 5, 12),
        Kind = kind,
        Attachment = new ArchiveAttachment(fileName, "application/xml", Encoding.UTF8.GetBytes("<ledger/>")),
    };
}
