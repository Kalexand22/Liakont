namespace Liakont.Modules.Pipeline.Tests.Unit.B2cReporting;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Liakont.Modules.Pipeline.Infrastructure.B2cReporting;
using Liakont.Modules.Pipeline.Tests.Unit.Check;
using Liakont.Modules.TvaMapping.Contracts.Services;
using Xunit;

/// <summary>
/// Couvre l'ORCHESTRATION du handler de récap de marge (<see cref="GetDocumentMarginRecapHandler"/>) : garde de
/// régime (buyer-indépendant), résolution tenant-scopée, fail-closed, conversion HT et assemblage du DTO — la
/// jonction qui produit le chiffre affiché à l'opérateur (aide à la déclaration de TVA). Réutilise les doubles
/// du CHECK (faux mapping TVA + faux tenant) — aucune dépendance d'I/O.
/// </summary>
public sealed class GetDocumentMarginRecapHandlerTests
{
    private static readonly Guid Company = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Handle_MarginDocument_ReturnsRecap_WithReconciledHtAndVat()
    {
        // Bordereau marge, honoraire acheteur 10 TTC, taux mappé 20 % → récap : 8,33 HT + 1,67 TVA (réconcilie 10).
        var handler = new GetDocumentMarginRecapHandler(MappedAt(20m), Tenant(Company));

        var result = await handler.Handle(new GetDocumentMarginRecapQuery { Pivot = MarginPivot(buyerFeeTtc: 10m) }, CancellationToken.None);

        result.Should().NotBeNull();
        result!.BuyerFeesTtc.Should().Be(10m);
        result.MarginTtc.Should().Be(10m);
        result.RatePercent.Should().Be(20m);
        result.BaseHt.Should().Be(8.33m);
        result.Tva.Should().Be(1.67m);
        (result.BaseHt + result.Tva).Should().Be(result.MarginTtc, "HT + TVA réconcilient le TTC (jamais de dérive)");
    }

    [Fact]
    public async Task Handle_NonMarginDocument_ReturnsNull()
    {
        // Document TAXABLE (TVA distincte) → IsMarginRegime faux → pas de récap (null), jamais un chiffre deviné.
        var taxable = new PivotDocumentDto(
            sourceDocumentKind: "invoice",
            number: "T-1",
            issueDate: new DateTime(2026, 6, 1),
            sourceReference: "src/T-1",
            supplier: null,
            totals: new PivotTotalsDto(totalNet: 100m, totalTax: 20m, totalGross: 120m),
            operationCategory: null,
            lines:
            [
                new PivotLineDto(
                    "Honoraires acheteur",
                    100m,
                    sourceRegimeCodes: ["5"],
                    taxes: [new PivotLineTaxDto(taxAmount: 20m, rate: 20m, categoryCode: VatCategory.S)],
                    role: PivotLineRole.BuyerFee),
            ]);
        var handler = new GetDocumentMarginRecapHandler(MappedAt(20m), Tenant(Company));

        var result = await handler.Handle(new GetDocumentMarginRecapQuery { Pivot = taxable }, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NoCurrentCompany_ReturnsNull()
    {
        var handler = new GetDocumentMarginRecapHandler(MappedAt(20m), Tenant(null));

        var result = await handler.Handle(new GetDocumentMarginRecapQuery { Pivot = MarginPivot(10m) }, CancellationToken.None);

        result.Should().BeNull("aucune société courante → pas de récap");
    }

    [Fact]
    public async Task Handle_UnmappedRate_ReturnsNull_FailClosed()
    {
        // Table de mapping absente → taux non résolu → blocage fail-closed → pas de récap (CLAUDE.md n°2/3).
        var handler = new GetDocumentMarginRecapHandler(NoTable(), Tenant(Company));

        var result = await handler.Handle(new GetDocumentMarginRecapQuery { Pivot = MarginPivot(10m) }, CancellationToken.None);

        result.Should().BeNull();
    }

    private static PivotDocumentDto MarginPivot(decimal buyerFeeTtc) => new(
        sourceDocumentKind: "B",
        number: "9000004",
        issueDate: new DateTime(2026, 6, 26),
        sourceReference: "encheresv6:ba:9000004",
        supplier: null,
        totals: new PivotTotalsDto(totalNet: 100m + buyerFeeTtc, totalTax: 0m, totalGross: 100m + buyerFeeTtc),
        operationCategory: null,
        lines:
        [
            new PivotLineDto(
                "Adjudication lot 2",
                100m,
                sourceRegimeCodes: ["6"],
                taxes: [new PivotLineTaxDto(taxAmount: 0m, rate: 0m, categoryCode: VatCategory.E, vatexCode: "VATEX-EU-J")]),
            new PivotLineDto(
                "Honoraires acheteur",
                buyerFeeTtc,
                sourceRegimeCodes: ["6"],
                taxes: [new PivotLineTaxDto(taxAmount: 0m, rate: 0m, categoryCode: VatCategory.E, vatexCode: "VATEX-EU-J")],
                role: PivotLineRole.BuyerFee),
        ]);

    private static CheckTestDoubles.FakeTenantSettingsQueries Tenant(Guid? companyId) => new(companyId);

    private static CheckTestDoubles.FakeTvaMappingService MappedAt(decimal rate) => new(new DocumentTvaMappingResult
    {
        TableExists = true,
        IsValidated = true,
        MappingVersion = "test",
        Lines = [new TvaLineMappingResult { SourceRegimeCode = "6", LineRef = "0", IsMapped = true, Category = "S", Rate = rate, Vatex = null }],
    });

    private static CheckTestDoubles.FakeTvaMappingService NoTable() => new(new DocumentTvaMappingResult
    {
        TableExists = false,
        IsValidated = false,
        MappingVersion = "none",
        Lines = [],
    });
}
