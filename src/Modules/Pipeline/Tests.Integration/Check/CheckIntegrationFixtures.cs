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
    private static readonly string[] NormalRegimeCodes = ["NORMAL"];

    public static string PayloadHashOf(PivotDocumentDto pivot) => PayloadHasher.ComputeHash(CanonicalJson.Serialize(pivot));

    public static PivotDocumentDto BuildPivot(string sourceReference, string regimeCode, PivotPartyDto? customer = null)
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
            number: "F-2026-" + ((uint)sourceReference.GetHashCode(StringComparison.Ordinal)).ToString("D10", CultureInfo.InvariantCulture),
            issueDate: new DateTime(2026, 1, 10),
            sourceReference: sourceReference,
            supplier: new PivotPartyDto("Étude Fictïve SVV"),
            totals: new PivotTotalsDto(120.00m, 24.00m, 144.00m, 144.00m),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: customer,
            lines: new[] { line });
    }

    /// <summary>
    /// Acheteur présentant un indice « professionnel » (champ société + forme juridique « SARL ») — déclenche
    /// le garde-fou B2B/B2C (VAL05) SANS dépendre du mapping TVA. Aucun SIREN/pays : seul ce garde-fou bloque.
    /// </summary>
    public static PivotPartyDto ProfessionalBuyer() => new("Client SARL", isCompanyHint: true);

    /// <summary>
    /// Construit une AUTO-FACTURE sous mandat (<c>IsSelfBilled</c>, MND07) : le <c>Supplier</c> EST le mandant
    /// (vendeur fiscal BG-4 → BT-30/BT-31), l'<c>Invoicer</c> est le tenant mandataire qui émet matériellement
    /// (art. 289 I-2 CGI). Le numéro SOURCE est distinct du BT-1 fiscal (alloué séparément par mandant — MND05).
    /// Valeurs fictives (CLAUDE.md n°7).
    /// </summary>
    public static PivotDocumentDto BuildSelfBilledPivot(string sourceReference, string mandantSiren, string? mandantVatNumber)
    {
        var line = new PivotLineDto(
            description: "Vente sous mandat — lot criée",
            netAmount: 120.00m,
            quantity: 1m,
            unitPriceNet: 120.00m,
            sourceRegimeCodes: NormalRegimeCodes,
            taxes: new[] { new PivotLineTaxDto(24.00m, 20m) },
            sourceLineRef: "ligne#1");

        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "SRC-" + ((uint)sourceReference.GetHashCode(StringComparison.Ordinal)).ToString("D10", CultureInfo.InvariantCulture),
            issueDate: new DateTime(2026, 1, 10),
            sourceReference: sourceReference,
            supplier: new PivotPartyDto("Armement Mandant Fictif", siren: mandantSiren, vatNumber: mandantVatNumber),
            totals: new PivotTotalsDto(120.00m, 24.00m, 144.00m, 144.00m),
            operationCategory: OperationCategory.LivraisonBiens,
            lines: new[] { line },
            invoicer: new PivotPartyDto("Étude Mandataire Fictïve", siren: "404833048"),
            isSelfBilled: true);
    }

    /// <summary>
    /// Construit un AVOIR (pivot) référençant une ou plusieurs factures d'origine — montants POSITIFS (la nature
    /// « avoir » est portée par le type, jamais par le signe, F07-F08 §B.2). Le code régime est explicite
    /// (« NORMAL » = mappé sur la table validée des harnais ; un code absent de la table fait bloquer le document
    /// au mapping, indépendamment de l'origine). Un avoir groupé porte plusieurs <see cref="PivotDocumentRefDto"/>
    /// (F07-F08 §B.4).
    /// </summary>
    public static PivotDocumentDto BuildCreditNote(string sourceReference, string regimeCode, params PivotDocumentRefDto[] originRefs)
    {
        var line = new PivotLineDto(
            description: "Avoir — annulation adjudication lot 7",
            netAmount: 120.00m,
            quantity: 1m,
            unitPriceNet: 120.00m,
            sourceRegimeCodes: new[] { regimeCode },
            taxes: new[] { new PivotLineTaxDto(24.00m, 20m) },
            sourceLineRef: "ligne#1");

        return new PivotDocumentDto(
            sourceDocumentKind: "A",
            number: "A-2026-" + ((uint)sourceReference.GetHashCode(StringComparison.Ordinal)).ToString("D10", CultureInfo.InvariantCulture),
            issueDate: new DateTime(2026, 1, 20),
            sourceReference: sourceReference,
            supplier: new PivotPartyDto("Étude Fictïve SVV"),
            totals: new PivotTotalsDto(120.00m, 24.00m, 144.00m, 144.00m),
            operationCategory: OperationCategory.LivraisonBiens,
            lines: new[] { line },
            creditNoteRefs: originRefs);
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
