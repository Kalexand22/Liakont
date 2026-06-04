namespace Liakont.Modules.Documents.Infrastructure;

using System;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Documents.Domain.Entities;
using Liakont.Modules.Ingestion.Contracts;

/// <summary>
/// Projette un document accepté par l'ingestion (<see cref="DetectedDocumentIntake"/>, contrat PIV04)
/// vers l'agrégat <see cref="Document"/> en état <c>Detected</c> (item TRK01). Mapping PUR (sans état,
/// sans I/O) : il REPORTE les montants calculés par la source et le type BRUT, sans calcul ni
/// classification (frontière module-rules §2 ; la classification facture/avoir et le contrôle des
/// totaux vivent dans Validation). Fonction isolée pour être testée en unitaire.
/// </summary>
internal static class DetectedDocumentMapper
{
    public static Document ToDetectedDocument(DetectedDocumentIntake input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var pivot = input.Document;
        ArgumentNullException.ThrowIfNull(pivot);

        return Document.CreateDetected(
            id: input.DocumentId,
            sourceReference: input.SourceReference,
            documentNumber: pivot.Number,
            documentType: pivot.SourceDocumentKind,
            issueDate: DateOnly.FromDateTime(pivot.IssueDate),
            supplierSiren: pivot.Supplier?.Siren,
            customerName: pivot.Customer?.Name,
            customerIsCompanyHint: pivot.Customer?.IsCompanyHint ?? false,
            totalNet: pivot.Totals.TotalNet,
            totalTax: pivot.Totals.TotalTax,
            totalGross: pivot.Totals.TotalGross,
            payloadHash: input.PayloadHash,
            detectedAtUtc: input.ReceivedAtUtc);
    }
}
