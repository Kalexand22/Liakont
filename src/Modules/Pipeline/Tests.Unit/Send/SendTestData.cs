namespace Liakont.Modules.Pipeline.Tests.Unit.Send;

using System;
using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>Fabriques de données pour les tests du SEND (PIP01c) : documents, pivots, comptes PA.</summary>
internal static class SendTestData
{
    public const string TenantSlug = "acme";

    public const string MappingVersion = "cmp-v1";

    public static readonly DateTimeOffset Now = new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private static readonly string[] NormalRegime = ["NORMAL"];

    private static readonly string[] ReducedRegime = ["REDUIT"];

    public static DocumentDto Document(
        Guid id,
        string state,
        string number = "F-2026-0007",
        string payloadHash = "hash-0007",
        string? paDocumentId = null,
        string? mappingVersion = MappingVersion)
    {
        return new DocumentDto
        {
            Id = id,
            SourceReference = "no_ba=4007",
            DocumentNumber = number,
            DocumentType = "Invoice",
            IssueDate = new DateOnly(2026, 1, 10),
            CustomerIsCompanyHint = true,
            TotalNet = 120.00m,
            TotalTax = 24.00m,
            TotalGross = 144.00m,
            State = state,
            PayloadHash = payloadHash,
            PaDocumentId = paDocumentId,
            MappingVersion = mappingVersion,
            FirstSeenUtc = Now,
            LastUpdateUtc = Now,
        };
    }

    public static DocumentSummaryDto Summary(Guid id, string state, string number = "F-2026-0007")
    {
        return new DocumentSummaryDto
        {
            Id = id,
            DocumentNumber = number,
            DocumentType = "Invoice",
            IssueDate = new DateOnly(2026, 1, 10),
            TotalGross = 144.00m,
            State = state,
            LastUpdateUtc = Now,
        };
    }

    /// <summary>Pivot mono-ligne (taux 20 %) — forme nominale d'une facture.</summary>
    public static PivotDocumentDto SingleLinePivot(string number = "F-2026-0007")
    {
        var line = new PivotLineDto(
            description: "Adjudication lot 7",
            netAmount: 120.00m,
            quantity: 1m,
            unitPriceNet: 120.00m,
            sourceRegimeCodes: NormalRegime,
            taxes: new[] { new PivotLineTaxDto(24.00m, 20m, VatCategory.S) },
            sourceLineRef: "ligne#1");

        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: number,
            issueDate: new DateTime(2026, 1, 10),
            sourceReference: "no_ba=4007",
            supplier: new PivotPartyDto("Étude Fictïve SVV", siren: "404833048"),
            totals: new PivotTotalsDto(120.00m, 24.00m, 144.00m),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: new PivotPartyDto("Client SARL", isCompanyHint: true),
            lines: new[] { line });
    }

    /// <summary>Pivot multi-lignes (deux lignes à 20 %, une à 10 %) — pour la ventilation TVA du rendu lisible.</summary>
    public static PivotDocumentDto MultiRatePivot(string number = "F-2026-0042")
    {
        var lines = new List<PivotLineDto>
        {
            new(
                description: "Lot A",
                netAmount: 100.00m,
                quantity: 1m,
                unitPriceNet: 100.00m,
                sourceRegimeCodes: NormalRegime,
                taxes: new[] { new PivotLineTaxDto(20.00m, 20m, VatCategory.S) },
                sourceLineRef: "l1"),
            new(
                description: "Lot B",
                netAmount: 50.00m,
                quantity: 1m,
                unitPriceNet: 50.00m,
                sourceRegimeCodes: NormalRegime,
                taxes: new[] { new PivotLineTaxDto(10.00m, 20m, VatCategory.S) },
                sourceLineRef: "l2"),
            new(
                description: "Lot C (taux réduit)",
                netAmount: 200.00m,
                quantity: 1m,
                unitPriceNet: 200.00m,
                sourceRegimeCodes: ReducedRegime,
                taxes: new[] { new PivotLineTaxDto(20.00m, 10m, VatCategory.AA) },
                sourceLineRef: "l3"),
        };

        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: number,
            issueDate: new DateTime(2026, 1, 10),
            sourceReference: "no_ba=4042",
            supplier: new PivotPartyDto("Étude Fictïve SVV", siren: "404833048"),
            totals: new PivotTotalsDto(350.00m, 50.00m, 400.00m),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: new PivotPartyDto("Client SARL", isCompanyHint: true),
            lines: lines);
    }

    /// <summary>
    /// Pivot porteur du marqueur de flux 10.3 (déclaration e-reporting B2C, B2C01) — mono-ligne, émetteur
    /// renseigné (le seul motif de maintien testé est l'absence de capacité B2C de la PA, jamais l'émetteur).
    /// </summary>
    public static PivotDocumentDto B2cReportingDeclarationPivot(string number = "B2C-2026-0007")
    {
        var line = new PivotLineDto(
            description: "Adjudication lot 7 (déclaration 10.3)",
            netAmount: 120.00m,
            quantity: 1m,
            unitPriceNet: 120.00m,
            sourceRegimeCodes: NormalRegime,
            taxes: new[] { new PivotLineTaxDto(24.00m, 20m, VatCategory.S) },
            sourceLineRef: "ligne#1");

        return new PivotDocumentDto(
            sourceDocumentKind: "DECLARATION",
            number: number,
            issueDate: new DateTime(2026, 1, 10),
            sourceReference: "no_ba=4007",
            supplier: new PivotPartyDto("Étude Fictïve SVV", siren: "404833048"),
            totals: new PivotTotalsDto(120.00m, 24.00m, 144.00m),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: new PivotPartyDto("Client SARL", isCompanyHint: true),
            lines: new[] { line },
            isB2cReportingDeclaration: true);
    }

    /// <summary>Pivot SANS émetteur ni nature d'opération (forme allégée AGT03) — la plateforme remplit au read-time (RB9).</summary>
    public static PivotDocumentDto SupplierLessPivot(string number = "F-2026-0007")
    {
        var line = new PivotLineDto(
            description: "Adjudication lot 7",
            netAmount: 120.00m,
            quantity: 1m,
            unitPriceNet: 120.00m,
            sourceRegimeCodes: NormalRegime,
            taxes: new[] { new PivotLineTaxDto(24.00m, 20m, VatCategory.S) },
            sourceLineRef: "ligne#1");

        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: number,
            issueDate: new DateTime(2026, 1, 10),
            sourceReference: "no_ba=4007",
            supplier: null,
            totals: new PivotTotalsDto(120.00m, 24.00m, 144.00m),
            operationCategory: null,
            customer: new PivotPartyDto("Client SARL", isCompanyHint: true),
            lines: new[] { line });
    }

    /// <summary>Profil tenant (émetteur) renseigné — source du remplissage read-time au SEND (RB9).</summary>
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
    public static FiscalSettingsDto FiscalSettings(string operationCategory = "LivraisonBiens") =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            OperationCategory = operationCategory,
            CreatedAt = Now,
        };

    public static PaAccountDto ActiveAccount(string pluginType = "Fake", bool isActive = true)
    {
        return new PaAccountDto
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            PluginType = pluginType,
            Environment = "Staging",
            AccountIdentifiers = "{}",
            HasApiKey = true,
            IsActive = isActive,
            CreatedAt = Now,
        };
    }
}
