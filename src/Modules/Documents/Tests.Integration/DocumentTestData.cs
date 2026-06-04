namespace Liakont.Modules.Documents.Tests.Integration;

using System;
using Liakont.Modules.Documents.Domain.Entities;

/// <summary>Fabriques de documents de test (partagées par les tests d'intégration).</summary>
internal static class DocumentTestData
{
    public static readonly DateTimeOffset DetectedAt = new(2026, 5, 14, 8, 0, 0, TimeSpan.Zero);

    public static Document NewDetected(
        Guid? id = null,
        string documentNumber = "F-2026-001",
        string sourceReference = "SRC-1",
        string documentType = "FAC",
        string? supplierSiren = "123456789",
        string? customerName = "Client SARL",
        bool customerIsCompanyHint = true,
        decimal totalNet = 100.00m,
        decimal totalTax = 20.00m,
        decimal totalGross = 120.00m,
        string payloadHash = "hash-1",
        DateTimeOffset? detectedAt = null)
    {
        return Document.CreateDetected(
            id ?? Guid.NewGuid(),
            sourceReference,
            documentNumber,
            documentType,
            new DateOnly(2026, 5, 14),
            supplierSiren,
            customerName,
            customerIsCompanyHint,
            totalNet,
            totalTax,
            totalGross,
            payloadHash,
            detectedAt ?? DetectedAt);
    }

    /// <summary>
    /// Reconstitue un document dans un ÉTAT DONNÉ (seeding des tests d'anti-doublon / lookups / altération,
    /// item TRK03) sans repasser par la machine à états : on a besoin de documents déjà <c>Issued</c> /
    /// <c>RejectedByPa</c> en base, indépendamment du chemin de transition (testé ailleurs).
    /// </summary>
    public static Document Reconstituted(
        DocumentState state,
        Guid? id = null,
        string documentNumber = "F-2026-001",
        string sourceReference = "SRC-1",
        string? supplierSiren = "123456789",
        string payloadHash = "hash-1",
        DateTimeOffset? at = null)
    {
        var moment = at ?? DetectedAt;
        return Document.Reconstitute(
            id ?? Guid.NewGuid(),
            sourceReference,
            documentNumber,
            "FAC",
            new DateOnly(2026, 5, 14),
            supplierSiren,
            "Client SARL",
            customerIsCompanyHint: true,
            totalNet: 100.00m,
            totalTax: 20.00m,
            totalGross: 120.00m,
            state,
            payloadHash,
            paDocumentId: state == DocumentState.Issued ? "PA-1" : null,
            mappingVersion: null,
            firstSeenUtc: moment,
            lastUpdateUtc: moment);
    }
}
