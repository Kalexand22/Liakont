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

    /// <summary>
    /// Pivot self-billed (autofacturation sous mandat, <c>IsSelfBilled = true</c>) — déclenche la garde
    /// d'émission MND03 : le contenu est par ailleurs valide (mono-ligne nominale), seul l'état d'acceptation
    /// décide de l'émissibilité.
    /// </summary>
    public static PivotDocumentDto SelfBilledSingleLinePivot(string regimeCode = "NORMAL", decimal net = 120.00m, decimal tax = 24.00m, decimal rate = 20m)
    {
        var line = new PivotLineDto(
            description: "Adjudication lot 7",
            netAmount: net,
            quantity: 1m,
            unitPriceNet: net,
            sourceRegimeCodes: new[] { regimeCode },
            taxes: new[] { new PivotLineTaxDto(tax, rate) },
            sourceLineRef: "ligne#1");

        return BuildPivot(new[] { line }, net, tax, isSelfBilled: true);
    }

    public static PivotDocumentDto BuildPivot(IReadOnlyList<PivotLineDto> lines, decimal totalNet, decimal totalTax, bool isSelfBilled = false)
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
            lines: lines,
            isSelfBilled: isSelfBilled);
    }

    /// <summary>Pivot SANS émetteur ni nature d'opération (forme allégée AGT03) — la plateforme remplit au read-time (RB9).</summary>
    public static PivotDocumentDto EmitterlessPivot(string regimeCode = "NORMAL", decimal net = 120.00m, decimal tax = 24.00m, decimal rate = 20m)
    {
        var line = new PivotLineDto(
            description: "Adjudication lot 7",
            netAmount: net,
            quantity: 1m,
            unitPriceNet: net,
            sourceRegimeCodes: new[] { regimeCode },
            taxes: new[] { new PivotLineTaxDto(tax, rate) },
            sourceLineRef: "ligne#1");

        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "F-2026-0007",
            issueDate: new DateTime(2026, 1, 10),
            sourceReference: "no_ba=4007",
            supplier: null,
            totals: new PivotTotalsDto(net, tax, net + tax),
            operationCategory: null,
            customer: new PivotPartyDto("Client SARL", isCompanyHint: true),
            lines: new[] { line });
    }

    /// <summary>Profil tenant (émetteur) renseigné — source du remplissage read-time au CHECK (RB9).</summary>
    public static TenantProfileDto EmitterProfile(string siren = "802193904", string raisonSociale = "SEM Keroman") =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            Siren = siren,
            RaisonSociale = raisonSociale,
            Street = "1 quai du Port",
            PostalCode = "56100",
            City = "Lorient",
            Country = "FR",
            Statut = "Actif",
            CreatedAt = Now,
        };

    /// <summary>Paramétrage fiscal (nature d'opération) — source du remplissage read-time de l'operationCategory.</summary>
    public static FiscalSettingsDto FiscalSettingsOf(string operationCategory) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            OperationCategory = operationCategory,
            CreatedAt = Now,
        };

    /// <summary>
    /// Pivot d'un bordereau au RÉGIME DE LA MARGE (B2C, art. 297 E, L2) : adjudication + honoraire acheteur EN
    /// LIGNE (rôle <see cref="PivotLineRole.BuyerFee"/>), aucune TVA distincte (TotalTax 0), acheteur PARTICULIER
    /// (B2C — ni SIREN ni indice société). Les régimes source sont mappés E + VATEX-EU-J par
    /// <see cref="MarginMappedResult"/> ; le taux de CALCUL des honoraires (B4) = 20 %.
    /// </summary>
    public static PivotDocumentDto MarginPivot(decimal adjudication = 100.00m, decimal buyerFeeTtc = 10.00m)
    {
        string[] marginRegimeCodes = ["6"];

        var adjudicationLine = new PivotLineDto(
            description: "Adjudication lot 2",
            netAmount: adjudication,
            quantity: 1m,
            unitPriceNet: adjudication,
            sourceRegimeCodes: marginRegimeCodes,
            taxes: new[] { new PivotLineTaxDto(0m, 0m) },
            sourceLineRef: "0");

        var buyerFeeLine = new PivotLineDto(
            description: "Honoraires acheteur",
            netAmount: buyerFeeTtc,
            quantity: 1m,
            unitPriceNet: buyerFeeTtc,
            sourceRegimeCodes: marginRegimeCodes,
            taxes: new[] { new PivotLineTaxDto(0m, 0m) },
            sourceLineRef: "1",
            role: PivotLineRole.BuyerFee);

        return new PivotDocumentDto(
            sourceDocumentKind: "B",
            number: "9000004",
            issueDate: new DateTime(2026, 6, 26),
            sourceReference: "encheresv6:ba:9000004",
            supplier: new PivotPartyDto("Étude Fictïve SVV", siren: "111111111"),
            totals: new PivotTotalsDto(adjudication + buyerFeeTtc, 0m, adjudication + buyerFeeTtc),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: new PivotPartyDto("Particulier"),
            lines: new[] { adjudicationLine, buyerFeeLine });
    }

    /// <summary>
    /// Résultat de mapping d'un bordereau marge : les DEUX lignes (adjudication + honoraire acheteur) mappées
    /// E + VATEX-EU-J (régime de la marge, F03 §2.2). Le taux (20 %) est le taux de CALCUL des honoraires (B4,
    /// F03 §2.3) — la marge reste sans TVA distincte (297 E). Une ligne par requête de <see cref="MarginPivot"/>.
    /// </summary>
    public static DocumentTvaMappingResult MarginMappedResult(string version = "cmp-v1", decimal feeRate = 20m)
    {
        TvaLineMappingResult Line(string lineRef) => new()
        {
            SourceRegimeCode = "6",
            LineRef = lineRef,
            IsMapped = true,
            Category = "E",
            Rate = feeRate,
            Vatex = "VATEX-EU-J",
        };

        return new DocumentTvaMappingResult
        {
            TableExists = true,
            IsValidated = true,
            MappingVersion = version,
            Lines = new[] { Line("0"), Line("1") },
        };
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

    public static DocumentTvaMappingResult MissingTableResult()
    {
        // Reproduit ce que renvoie le service quand aucune table n'existe : TableExists=false, chaque ligne
        // bloquée. CHECK court-circuite sur !TableExists avant d'évaluer les lignes (motif « créez la table »).
        var line = new TvaLineMappingResult
        {
            SourceRegimeCode = "NORMAL",
            LineRef = "0",
            IsMapped = false,
            BlockReason = "Aucune table de mapping TVA n'est définie pour ce tenant.",
        };

        return new DocumentTvaMappingResult
        {
            TableExists = false,
            IsValidated = false,
            MappingVersion = null,
            Lines = new[] { line },
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
