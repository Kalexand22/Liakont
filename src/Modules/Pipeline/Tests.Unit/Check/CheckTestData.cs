namespace Liakont.Modules.Pipeline.Tests.Unit.Check;

using System;
using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Ingestion.Contracts.Events;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TvaMapping.Contracts.Services;
using Stratum.Common.Abstractions.Events;

/// <summary>Fabriques de données de test pour le CHECK (pivots, résultats de mapping, document, événement).</summary>
internal static class CheckTestData
{
    public const string TenantSlug = "acme";

    public static readonly DateTimeOffset Now = new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Pivot d'une facture mono-ligne (1 code régime ↔ 1 ventilation) — forme nominale du contrat.</summary>
    public static PivotDocumentDto SingleLinePivot(string regimeCode = "NORMAL", decimal net = 120.00m, decimal tax = 24.00m, decimal rate = 20m)
    {
        var line = new PivotLineDto(
            description: "Adjudication lot 7",
            netAmount: net,
            quantity: 1m,
            unitPriceNet: net,
            sourceRegimeCodes: new[] { regimeCode },
            taxes: new[] { new PivotLineTaxDto(tax, rate) },
            sourceLineRef: "ligne#1");

        return BuildPivot(new[] { line }, net, tax);
    }

    public static PivotDocumentDto BuildPivot(IReadOnlyList<PivotLineDto> lines, decimal totalNet, decimal totalTax)
    {
        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "F-2026-0007",
            issueDate: new DateTime(2026, 1, 10),
            sourceReference: "no_ba=4007",
            supplier: new PivotPartyDto("Étude Fictïve SVV", siren: "111111111"),
            totals: new PivotTotalsDto(totalNet, totalTax, totalNet + totalTax),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: new PivotPartyDto("Client SARL", isCompanyHint: true),
            lines: lines);
    }

    public static DocumentTvaMappingResult MappedResult(string version = "cmp-v1", bool isValidated = true)
    {
        var line = new TvaLineMappingResult
        {
            SourceRegimeCode = "NORMAL",
            LineRef = "0",
            IsMapped = true,
            Category = "S",
            Rate = 20m,
            Vatex = null,
        };

        return new DocumentTvaMappingResult
        {
            TableExists = true,
            IsValidated = isValidated,
            MappingVersion = version,
            Lines = new[] { line },
        };
    }

    public static DocumentTvaMappingResult BlockedLineResult(string version = "cmp-v1", bool isValidated = true)
    {
        var line = new TvaLineMappingResult
        {
            SourceRegimeCode = "INCONNU",
            LineRef = "0",
            IsMapped = false,
            BlockReason = "Régime de TVA source « INCONNU » absent de la table de mapping.",
        };

        return new DocumentTvaMappingResult
        {
            TableExists = true,
            IsValidated = isValidated,
            MappingVersion = version,
            Lines = new[] { line },
        };
    }

    public static DocumentTvaMappingResult MissingTableResult(IReadOnlyList<TvaLineMappingResult> lines)
    {
        return new DocumentTvaMappingResult
        {
            TableExists = false,
            IsValidated = false,
            MappingVersion = null,
            Lines = lines,
        };
    }

    public static DocumentDto Document(Guid id, string state)
    {
        return new DocumentDto
        {
            Id = id,
            SourceReference = "no_ba=4007",
            DocumentNumber = "F-2026-0007",
            DocumentType = "Invoice",
            IssueDate = new DateOnly(2026, 1, 10),
            CustomerIsCompanyHint = true,
            TotalNet = 120.00m,
            TotalTax = 24.00m,
            TotalGross = 144.00m,
            State = state,
            PayloadHash = "hash-0007",
            FirstSeenUtc = Now,
            LastUpdateUtc = Now,
        };
    }

    public static PaAccountDto PaAccount(string environment, bool isActive)
    {
        return new PaAccountDto
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            PluginType = "fake",
            Environment = environment,
            AccountIdentifiers = "{}",
            HasApiKey = true,
            IsActive = isActive,
            CreatedAt = Now,
        };
    }

    public static IntegrationEvent<DocumentReceivedV1> Event(Guid documentId, string payloadHash = "hash-0007")
    {
        var payload = new DocumentReceivedV1
        {
            TenantId = TenantSlug,
            DocumentId = documentId,
            SourceReference = "no_ba=4007",
            PayloadHash = payloadHash,
            ReceivedAtUtc = Now,
        };

        return new IntegrationEvent<DocumentReceivedV1>
        {
            EventId = Guid.NewGuid(),
            EventType = "ingestion.document.received",
            OccurredAt = Now,
            CorrelationId = Guid.NewGuid(),
            ModuleSource = "Ingestion",
            Payload = payload,
            Version = 1,
        };
    }
}
