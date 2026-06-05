namespace Liakont.Modules.Pipeline.Tests.Integration.Check;

using System;
using System.Globalization;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Modules.Ingestion.Contracts.Events;
using Stratum.Common.Abstractions.Events;

/// <summary>Fabriques de données pour les tests d'intégration du CHECK (pivot, empreinte, événement).</summary>
internal static class CheckIntegrationFixtures
{
    public static string PayloadHashOf(PivotDocumentDto pivot) => PayloadHasher.ComputeHash(CanonicalJson.Serialize(pivot));

    public static PivotDocumentDto BuildPivot(string sourceReference, string regimeCode)
    {
        var line = new PivotLineDto(
            description: "Adjudication lot 7 — vase décoratif",
            netAmount: 120.00m,
            quantity: 1m,
            unitPriceNet: 120.00m,
            sourceRegimeCodes: new[] { regimeCode },
            taxes: new[] { new PivotLineTaxDto(24.00m, 20m) },
            sourceLineRef: "ligne#1");

        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "F-2026-" + Math.Abs(sourceReference.GetHashCode(StringComparison.Ordinal)).ToString("D8", CultureInfo.InvariantCulture),
            issueDate: new DateTime(2026, 1, 10),
            sourceReference: sourceReference,
            supplier: new PivotPartyDto("Étude Fictïve SVV"),
            totals: new PivotTotalsDto(120.00m, 24.00m, 144.00m, 144.00m),
            operationCategory: OperationCategory.LivraisonBiens,
            lines: new[] { line });
    }

    public static IntegrationEvent<DocumentReceivedV1> Event(Guid documentId, string sourceReference, string payloadHash)
    {
        var payload = new DocumentReceivedV1
        {
            TenantId = PipelineCheckHarness.TenantSlug,
            DocumentId = documentId,
            SourceReference = sourceReference,
            PayloadHash = payloadHash,
            ReceivedAtUtc = DateTimeOffset.UtcNow,
        };

        return new IntegrationEvent<DocumentReceivedV1>
        {
            EventId = Guid.NewGuid(),
            EventType = "ingestion.document.received",
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid(),
            ModuleSource = "Ingestion",
            Payload = payload,
            Version = 1,
        };
    }
}
