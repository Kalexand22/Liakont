namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.B2cReporting;
using Liakont.Host.Components.Pages;
using Liakont.Modules.Pipeline.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Tests bUnit de la page des émissions e-reporting B2C de la marge (B4, règle de review n°19 : une page
/// Blazor sans test est un P1). Couvre les états observables : liste avec badge d'état, état vide explicite,
/// bandeau d'erreur (sans fuite de données + filtre toujours disponible), rechargement par période, format
/// de date français. Aucune logique métier dans la page (déléguée au service) — seule la projection est testée.
/// </summary>
public sealed class B2cMarginEmissionsTests : BunitContext
{
    public B2cMarginEmissionsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs();
    }

    [Fact]
    public void Renders_Emissions_With_Status_Badge()
    {
        Services.AddScoped<IB2cMarginEmissionsConsoleQueries>(_ => FakeQueries.Returning(
            Model([Emission("Issued", paEmissionId: "591")])));

        var cut = Render<B2cMarginEmissions>();

        cut.Find("h1").TextContent.Should().Contain("Émissions");
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='margin-status-Issued']").Should().NotBeEmpty());
    }

    [Fact]
    public void Shows_An_Explicit_Empty_State_When_There_Are_No_Emissions()
    {
        Services.AddScoped<IB2cMarginEmissionsConsoleQueries>(_ => FakeQueries.Returning(Model([])));

        var cut = Render<B2cMarginEmissions>();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='margin-emissions-empty']").Should().ContainSingle());
    }

    [Fact]
    public void Shows_Error_Banner_When_Load_Throws()
    {
        Services.AddScoped<IB2cMarginEmissionsConsoleQueries>(_ => FakeQueries.Throwing());

        var cut = Render<B2cMarginEmissions>();

        // L'échec reste VISIBLE (bandeau) et n'expose AUCUNE donnée (ni liste, ni état vide), mais le filtre
        // période RESTE disponible pour réessayer (anti faux-vert + ergonomie).
        cut.FindAll("[data-testid='margin-emissions-error']").Should().ContainSingle();
        cut.FindAll("[data-testid='margin-emissions-filters']").Should().ContainSingle();
        cut.FindAll("[data-testid='margin-emissions-empty']").Should().BeEmpty();
    }

    [Fact]
    public void Formats_The_Aggregate_Date_In_French()
    {
        Services.AddScoped<IB2cMarginEmissionsConsoleQueries>(_ => FakeQueries.Returning(
            Model([Emission("Issued", paEmissionId: "591", date: new DateOnly(2026, 6, 23))])));

        var cut = Render<B2cMarginEmissions>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("23/06/2026"));
    }

    [Fact]
    public void Changing_The_Period_Reloads_The_Emissions_For_That_Month()
    {
        var spy = new SpyQueries(
            Model([Emission("Issued", paEmissionId: "591")]),
            Model([Emission("RejectedByPa", paEmissionId: null)]));
        Services.AddScoped<IB2cMarginEmissionsConsoleQueries>(_ => spy);

        var cut = Render<B2cMarginEmissions>();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='margin-status-Issued']").Should().NotBeEmpty());

        cut.Find("[data-testid='margin-emissions-filter-period']").Change("2026-01");

        cut.WaitForAssertion(() =>
        {
            spy.RequestedPeriods.Should().Contain("2026-01");
            cut.FindAll("[data-testid='margin-status-RejectedByPa']").Should().NotBeEmpty();
            cut.FindAll("[data-testid='margin-status-Issued']").Should().BeEmpty();
        });
    }

    private static B2cMarginEmissionsViewModel Model(IReadOnlyList<B2cMarginEmissionRow> emissions) =>
        new() { Emissions = emissions };

    private static B2cMarginEmissionRow Emission(
        string status,
        string? paEmissionId,
        DateOnly? date = null,
        int documentCount = 3) =>
        B2cMarginEmissionRow.FromDto(new B2cMarginEmissionAggregateDto
        {
            EmissionBatchId = Guid.NewGuid(),
            AggregateDate = date ?? new DateOnly(2026, 6, 1),
            CurrencyCode = "EUR",
            Category = "TMA1",
            Role = "SE",
            DocumentCount = documentCount,
            Status = status,
            PaEmissionId = paEmissionId,
            Detail = null,
            LastActivityUtc = new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.Zero),
            ContentHash = "hash-" + status,
        });

    private sealed class FakeQueries : IB2cMarginEmissionsConsoleQueries
    {
        private readonly B2cMarginEmissionsViewModel? _model;
        private readonly bool _throws;

        private FakeQueries(B2cMarginEmissionsViewModel? model, bool throws)
        {
            _model = model;
            _throws = throws;
        }

        public static FakeQueries Returning(B2cMarginEmissionsViewModel model) => new(model, throws: false);

        public static FakeQueries Throwing() => new(null, throws: true);

        public Task<B2cMarginEmissionsViewModel> GetEmissionsAsync(string? period, CancellationToken cancellationToken = default)
        {
            if (_throws)
            {
                throw new InvalidOperationException("Échec simulé de chargement des émissions de marge.");
            }

            return Task.FromResult(_model!);
        }

        public Task<B2cMarginEmissionDetailViewModel?> GetEmissionDetailAsync(Guid emissionBatchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<B2cMarginEmissionDetailViewModel?>(null);
    }

    private sealed class SpyQueries : IB2cMarginEmissionsConsoleQueries
    {
        private readonly B2cMarginEmissionsViewModel _firstLoad;
        private readonly B2cMarginEmissionsViewModel _afterPeriodChange;
        private int _calls;

        public SpyQueries(B2cMarginEmissionsViewModel firstLoad, B2cMarginEmissionsViewModel afterPeriodChange)
        {
            _firstLoad = firstLoad;
            _afterPeriodChange = afterPeriodChange;
        }

        public List<string?> RequestedPeriods { get; } = [];

        public Task<B2cMarginEmissionsViewModel> GetEmissionsAsync(string? period, CancellationToken cancellationToken = default)
        {
            RequestedPeriods.Add(period);
            var model = _calls == 0 ? _firstLoad : _afterPeriodChange;
            _calls++;
            return Task.FromResult(model);
        }

        public Task<B2cMarginEmissionDetailViewModel?> GetEmissionDetailAsync(Guid emissionBatchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<B2cMarginEmissionDetailViewModel?>(null);
    }
}
