namespace Liakont.Modules.Pipeline.Tests.Unit.B2cReporting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Infrastructure.B2cReporting;
using Liakont.Modules.TvaMapping.Contracts.Services;
using Xunit;

/// <summary>
/// Couvre le résolveur de marge PARTAGÉ (<see cref="B2cMarginDocumentResolver"/>) — source unique de l'assemblage
/// de la marge, utilisée par l'agrégat e-reporting B2C ET le récap marge du détail. Vérifie la ventilation
/// acheteur (lignes rôle BuyerFee) + vendeur (SellerFees), le taux UNIQUE des honoraires (mapping F03 Part.Frais),
/// et le fail-closed (taux non mappé). Le taux vient de la table validée, jamais inventé (CLAUDE.md n°2).
/// </summary>
public sealed class B2cMarginDocumentResolverTests
{
    private static readonly Guid Company = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task ResolveAsync_SumsBuyerAndSellerLegs_AtTheMappedRate()
    {
        // Bordereau marge : honoraire acheteur 10 TTC (ligne BuyerFee) + honoraire vendeur 5 TTC (SellerFees),
        // taux mappé 20 % → marge TTC 15, ventilation acheteur 10 / vendeur 5 exposée pour le récap.
        var pivot = MarginPivot(buyerFeeTtc: 10m, sellerFeeTtc: 5m);
        var mapping = MappedAt(20m);

        var resolution = await B2cMarginDocumentResolver.ResolveAsync(mapping, Company, pivot, CancellationToken.None);

        resolution.IsResolved.Should().BeTrue();
        resolution.BuyerFeesTtc.Should().Be(10m);
        resolution.SellerFeesTtc.Should().Be(5m);
        resolution.MarginTtc.Should().Be(15m);
        resolution.RatePercent.Should().Be(20m);
    }

    [Fact]
    public async Task ResolveAsync_BuyerLegOnly_WhenNoSellerFees()
    {
        var pivot = MarginPivot(buyerFeeTtc: 10m, sellerFeeTtc: null);
        var mapping = MappedAt(20m);

        var resolution = await B2cMarginDocumentResolver.ResolveAsync(mapping, Company, pivot, CancellationToken.None);

        resolution.IsResolved.Should().BeTrue();
        resolution.BuyerFeesTtc.Should().Be(10m);
        resolution.SellerFeesTtc.Should().Be(0m);
        resolution.MarginTtc.Should().Be(10m);
    }

    [Fact]
    public async Task ResolveAsync_Breakdown_AlwaysReconcilesToTheMargin()
    {
        // Invariant d'affichage : acheteur TTC + vendeur TTC == marge TTC (réconciliation par construction —
        // vendeur = résiduel), pour que le récap ne montre jamais un écart d'1 centime.
        var pivot = MarginPivot(buyerFeeTtc: 401.28m, sellerFeeTtc: 360.00m);
        var mapping = MappedAt(20m);

        var resolution = await B2cMarginDocumentResolver.ResolveAsync(mapping, Company, pivot, CancellationToken.None);

        resolution.IsResolved.Should().BeTrue();
        (resolution.BuyerFeesTtc + resolution.SellerFeesTtc).Should().Be(resolution.MarginTtc);
    }

    [Fact]
    public async Task ResolveAsync_FailsClosed_WhenRateIsNotMapped()
    {
        // Table absente → taux non résolu → blocage typé (UnmappedRate), jamais un taux deviné (CLAUDE.md n°2/3).
        var pivot = MarginPivot(buyerFeeTtc: 10m, sellerFeeTtc: 5m);
        var mapping = NoTable();

        var resolution = await B2cMarginDocumentResolver.ResolveAsync(mapping, Company, pivot, CancellationToken.None);

        resolution.IsResolved.Should().BeFalse();
        resolution.BlockReason.Should().NotBeNull();
    }

    private static PivotDocumentDto MarginPivot(decimal buyerFeeTtc, decimal? sellerFeeTtc)
    {
        var lines = new List<PivotLineDto>
        {
            new(
                description: "Adjudication lot 2",
                netAmount: 100m,
                sourceRegimeCodes: ["6"],
                taxes: [new PivotLineTaxDto(taxAmount: 0m, rate: 0m, categoryCode: VatCategory.E, vatexCode: "VATEX-EU-J")]),
            new(
                description: "Honoraires acheteur",
                netAmount: buyerFeeTtc,
                sourceRegimeCodes: ["6"],
                taxes: [new PivotLineTaxDto(taxAmount: 0m, rate: 0m, categoryCode: VatCategory.E, vatexCode: "VATEX-EU-J")],
                role: PivotLineRole.BuyerFee),
        };

        var sellerFees = sellerFeeTtc is { } seller
            ? new[] { new PivotSellerFeeDto("BV-1", seller, sourceRegimeCode: "6") }
            : null;

        return new PivotDocumentDto(
            sourceDocumentKind: "B",
            number: "9000004",
            issueDate: new DateTime(2026, 6, 26),
            sourceReference: "encheresv6:ba:9000004",
            supplier: null,
            totals: new PivotTotalsDto(totalNet: 100m + buyerFeeTtc, totalTax: 0m, totalGross: 100m + buyerFeeTtc),
            operationCategory: null,
            lines: lines,
            sellerFees: sellerFees);
    }

    private static FakeTvaMappingService MappedAt(decimal rate) => new(rate, tableExists: true);

    private static FakeTvaMappingService NoTable() => new(rate: 0m, tableExists: false);

    // Faux mapping TVA : mappe CHAQUE requête (Part.Frais) au taux donné si la table existe, sinon « table absente »
    // (taux non résolu) — suffit à exercer la résolution de marge sans la table réelle du tenant.
    private sealed class FakeTvaMappingService : ITvaMappingService
    {
        private readonly decimal _rate;
        private readonly bool _tableExists;

        public FakeTvaMappingService(decimal rate, bool tableExists)
        {
            _rate = rate;
            _tableExists = tableExists;
        }

        public Task<DocumentTvaMappingResult> MapAsync(
            Guid companyId,
            IReadOnlyList<TvaLineMappingRequest> lines,
            CancellationToken cancellationToken = default)
        {
            var results = lines
                .Select(request => new TvaLineMappingResult
                {
                    SourceRegimeCode = request.SourceRegimeCode,
                    LineRef = request.LineRef,
                    IsMapped = _tableExists,
                    Category = _tableExists ? "S" : null,
                    Rate = _tableExists ? _rate : null,
                    Vatex = null,
                })
                .ToList();

            return Task.FromResult(new DocumentTvaMappingResult
            {
                TableExists = _tableExists,
                IsValidated = _tableExists,
                MappingVersion = "test",
                Lines = results,
            });
        }
    }
}
