namespace Liakont.Host.Tests.Unit.TvaDeclaration;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.TvaDeclaration;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Xunit;

/// <summary>
/// Tests du service de composition de la page « TVA / Déclaration » (L2) : il lit le registre de la marge agrégé
/// par mois × devise × taux et projette en modèle de présentation AVEC les totaux. On vérifie la projection
/// fidèle (aucune valeur recalculée — CLAUDE.md n°2) et que les totaux sont la SOMME EXACTE des lignes (decimal,
/// jamais float — n°1). La période passée est relayée verbatim à la couche Query (filtre de date pur).
/// </summary>
public sealed class TvaDeclarationConsoleQueryServiceTests
{
    [Fact]
    public async Task Projects_Rows_Verbatim_And_Sums_Totals()
    {
        var queries = new FakeMarginRegistryQueries([
            Dto(ratePercent: 20m, baseHt: 100m, vat: 20m, documentCount: 2),
            Dto(ratePercent: 5.5m, baseHt: 50m, vat: 2.75m, documentCount: 1),
        ]);
        var service = new TvaDeclarationConsoleQueryService(queries);

        var model = await service.GetDeclarationAsync("2026-06");

        model.Lines.Should().HaveCount(2);
        model.Lines[0].RatePercent.Should().Be(20m);
        model.Lines[0].MarginBaseHt.Should().Be(100m);
        model.Lines[0].MarginVat.Should().Be(20m);
        model.Lines[0].DocumentCount.Should().Be(2);

        // Totaux = somme exacte des lignes (decimal half-up déjà figé en base ; aucune dérive).
        model.TotalBaseHt.Should().Be(150m);
        model.TotalVat.Should().Be(22.75m);
    }

    [Fact]
    public async Task Returns_Empty_Totals_When_No_Margin()
    {
        var service = new TvaDeclarationConsoleQueryService(new FakeMarginRegistryQueries([]));

        var model = await service.GetDeclarationAsync("2026-06");

        model.Lines.Should().BeEmpty();
        model.TotalBaseHt.Should().Be(0m);
        model.TotalVat.Should().Be(0m);
    }

    [Fact]
    public async Task Relays_The_Period_Filter_Verbatim()
    {
        var queries = new FakeMarginRegistryQueries([]);
        var service = new TvaDeclarationConsoleQueryService(queries);

        await service.GetDeclarationAsync("2026-01");

        queries.LastPeriod.Should().Be("2026-01");
    }

    private static MarginRegistryMonthlyDto Dto(decimal ratePercent, decimal baseHt, decimal vat, int documentCount) =>
        new()
        {
            Period = "2026-06",
            CurrencyCode = "EUR",
            RatePercent = ratePercent,
            MarginBaseHt = baseHt,
            MarginVat = vat,
            DocumentCount = documentCount,
        };

    private sealed class FakeMarginRegistryQueries : IMarginRegistryQueries
    {
        private readonly IReadOnlyList<MarginRegistryMonthlyDto> _rows;

        public FakeMarginRegistryQueries(IReadOnlyList<MarginRegistryMonthlyDto> rows) => _rows = rows;

        public string? LastPeriod { get; private set; }

        public Task<IReadOnlyList<MarginRegistryMonthlyDto>> GetMonthlyAsync(string? period, CancellationToken cancellationToken = default)
        {
            LastPeriod = period;
            return Task.FromResult(_rows);
        }
    }
}
