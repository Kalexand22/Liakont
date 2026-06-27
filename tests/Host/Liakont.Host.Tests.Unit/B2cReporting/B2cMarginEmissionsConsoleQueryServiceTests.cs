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

    [Fact]
    public async Task GetEmissionDetail_Projects_The_Dto_With_A_Readable_Pa_Motif_And_Document_Family()
    {
        var batchId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var detail = new B2cMarginEmissionDetailDto
        {
            EmissionBatchId = batchId,
            AggregateDate = new DateOnly(2026, 6, 23),
            CurrencyCode = "EUR",
            Category = "TMA1",
            Role = "SE",
            Status = "RejectedByPa",
            PaEmissionId = null,
            Detail = "Rejet par la plateforme.",
            PaResponseSnapshot = """{"http_status_code":400,"message":"cannot add transaction at date 2024-01-03"}""",
            LastActivityUtc = new DateTimeOffset(2026, 6, 23, 9, 0, 0, TimeSpan.Zero),
            ContentHash = "hash",
            Documents = [new B2cMarginEmissionDocumentDto { DocumentId = docId, SourceReference = "encheresv6:ba:9000004" }],
        };
        var fake = new FakeQueries([], detail);
        var service = new B2cMarginEmissionsConsoleQueryService(fake);

        var model = await service.GetEmissionDetailAsync(batchId);

        model.Should().NotBeNull();
        model!.PaEmissionId.Should().Be("—", "l'agrégat rejeté n'a pas d'id plateforme");
        model.PaResponseLines.Should().ContainSingle().Which.Should().Contain("cannot add transaction");
        model.Documents.Should().ContainSingle();
        model.Documents[0].Family.Should().Be("Bordereau acheteur", "la famille est dérivée de la référence source");
        fake.RequestedBatchIds.Should().ContainSingle().Which.Should().Be(batchId);
    }

    [Fact]
    public async Task GetEmissionDetail_Returns_Null_When_The_Batch_Is_Unknown()
    {
        var service = new B2cMarginEmissionsConsoleQueryService(new FakeQueries([], detail: null));

        (await service.GetEmissionDetailAsync(Guid.NewGuid())).Should().BeNull();
    }

    private sealed class FakeQueries : IB2cMarginEmissionQueries
    {
        private readonly IReadOnlyList<B2cMarginEmissionAggregateDto> _emissions;
        private readonly B2cMarginEmissionDetailDto? _detail;

        public FakeQueries(IReadOnlyList<B2cMarginEmissionAggregateDto> emissions, B2cMarginEmissionDetailDto? detail = null)
        {
            _emissions = emissions;
            _detail = detail;
        }

        public List<string?> RequestedPeriods { get; } = [];

        public List<Guid> RequestedBatchIds { get; } = [];

        public Task<IReadOnlyList<B2cMarginEmissionAggregateDto>> GetEmissionsAsync(string? period, CancellationToken cancellationToken = default)
        {
            RequestedPeriods.Add(period);
            return Task.FromResult(_emissions);
        }

        public Task<B2cMarginEmissionDetailDto?> GetEmissionDetailAsync(Guid emissionBatchId, CancellationToken cancellationToken = default)
        {
            RequestedBatchIds.Add(emissionBatchId);
            return Task.FromResult(_detail);
        }
    }
}
