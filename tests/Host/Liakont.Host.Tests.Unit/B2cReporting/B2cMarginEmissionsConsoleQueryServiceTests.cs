namespace Liakont.Host.Tests.Unit.B2cReporting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.B2cReporting;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Xunit;

/// <summary>
/// Tests du service de composition de la page des émissions de marge B2C : projection DTO → ligne de
/// présentation (formatage uniquement, aucune (re)dérivation fiscale), passage transparent du filtre période,
/// et rendu « — » d'un id plateforme / détail absent (tri réflexif de DeclaredListPage sur des colonnes non
/// nullables — même patron que PaymentAggregateRow).
/// </summary>
public sealed class B2cMarginEmissionsConsoleQueryServiceTests
{
    [Fact]
    public async Task Maps_Each_Dto_To_A_Row()
    {
        var service = Service(
            Dto("Issued", paEmissionId: "591", documentCount: 4),
            Dto("Pending", paEmissionId: null, documentCount: 1));

        var model = await service.GetEmissionsAsync(period: "2026-06");

        model.Emissions.Should().HaveCount(2);
        var issued = model.Emissions.Single(e => e.Status == "Issued");
        issued.PaEmissionId.Should().Be("591");
        issued.DocumentCount.Should().Be(4);
        issued.Category.Should().Be("TMA1");
        issued.Role.Should().Be("SE");
        issued.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task Renders_Missing_PaEmissionId_And_Detail_As_Dash()
    {
        var service = Service(Dto("Pending", paEmissionId: null, detail: null));

        var model = await service.GetEmissionsAsync(period: null);

        var row = model.Emissions.Single();
        row.PaEmissionId.Should().Be("—");
        row.Detail.Should().Be("—");
    }

    [Fact]
    public async Task Passes_The_Period_Through_To_The_Query()
    {
        var fake = new FakeQueries([]);
        var service = new B2cMarginEmissionsConsoleQueryService(fake);

        await service.GetEmissionsAsync(period: "2026-01");

        fake.RequestedPeriods.Should().ContainSingle().Which.Should().Be("2026-01");
    }

    private static B2cMarginEmissionsConsoleQueryService Service(params B2cMarginEmissionAggregateDto[] emissions) =>
        new(new FakeQueries(emissions));

    private static B2cMarginEmissionAggregateDto Dto(
        string status,
        string? paEmissionId,
        string? detail = null,
        int documentCount = 2) =>
        new()
        {
            EmissionBatchId = Guid.NewGuid(),
            AggregateDate = new DateOnly(2026, 6, 1),
            CurrencyCode = "EUR",
            Category = "TMA1",
            Role = "SE",
            DocumentCount = documentCount,
            Status = status,
            PaEmissionId = paEmissionId,
            Detail = detail,
            LastActivityUtc = new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.Zero),
            ContentHash = "hash-" + status,
        };

    private sealed class FakeQueries : IB2cMarginEmissionQueries
    {
        private readonly IReadOnlyList<B2cMarginEmissionAggregateDto> _emissions;

        public FakeQueries(IReadOnlyList<B2cMarginEmissionAggregateDto> emissions) => _emissions = emissions;

        public List<string?> RequestedPeriods { get; } = [];

        public Task<IReadOnlyList<B2cMarginEmissionAggregateDto>> GetEmissionsAsync(string? period, CancellationToken cancellationToken = default)
        {
            RequestedPeriods.Add(period);
            return Task.FromResult(_emissions);
        }
    }
}
