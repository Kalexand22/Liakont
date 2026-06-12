namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.Dashboard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Xunit;

public sealed class HomeTests : BunitContext
{
    public HomeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();

        // Le tableau de bord embarque le graphique « Année en cours » (Chart du design-system).
        Services.AddScoped<IChartRenderer, StubChartRenderer>();
    }

    [Fact]
    public void Should_Render_Dashboard_When_Load_Succeeds()
    {
        Services.AddScoped<IDashboardQueries>(_ => FakeDashboardQueries.Succeeding(BuildModel()));

        var cut = Render<Home>();

        cut.FindAll("[data-testid='liakont-dashboard']").Should().ContainSingle();
        cut.FindAll("[data-testid='dashboard-error']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_Error_Banner_When_Load_Throws()
    {
        Services.AddScoped<IDashboardQueries>(_ => FakeDashboardQueries.Throwing());

        var cut = Render<Home>();

        // L'échec d'assemblage reste VISIBLE (bandeau) et n'expose pas le tableau de bord (anti faux-vert).
        cut.FindAll("[data-testid='dashboard-error']").Should().ContainSingle();
        cut.FindAll("[data-testid='liakont-dashboard']").Should().BeEmpty();
    }

    [Fact]
    public async Task Clicking_A_Year_Chart_Bar_Should_Navigate_To_The_Year_Filtered_Documents_List()
    {
        Services.AddScoped<IDashboardQueries>(_ => FakeDashboardQueries.Succeeding(BuildModel()));

        var cut = Render<Home>();

        // Le clic vient du JS (chart.js) via OnChartPointClick : on l'invoque directement, comme le
        // ferait l'interop. DataIndex 0 = premier point = état brut « Detected » du périmètre année.
        var chart = cut.FindComponent<Stratum.Common.UI.Components.Chart<DashboardChartPoint>>();
        await cut.InvokeAsync(() => chart.Instance.OnChartPointClick("Documents", "Détecté", 0, dataIndex: 0));

        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.Uri.Should().EndWith(
            "/documents?etat=Detected&du=2026-01-01&au=2026-12-31",
            "la page navigue vers la liste filtrée sur l'état cliqué ET les bornes du périmètre année");
    }

    private static DashboardCounterScope Scope(string key, string label, DateOnly from, DateOnly to) => new()
    {
        Key = key,
        Label = label,
        From = from,
        To = to,
        Counts = [new DashboardStateCount("Detected", 0)],
    };

    private static DashboardViewModel BuildModel() => new()
    {
        ProfileConfigured = true,
        CurrentMonth = Scope("current-month", "Mois en cours", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)),
        PreviousMonth = Scope("previous-month", "Mois précédent", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31)),
        CurrentYear = Scope("current-year", "Année en cours", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
        Agents = [],
        TvaStatus = DashboardTvaStatus.NotConfigured,
        ReportingFrequency = null,
    };

    private sealed class FakeDashboardQueries : IDashboardQueries
    {
        private readonly DashboardViewModel? _model;
        private readonly bool _throws;

        private FakeDashboardQueries(DashboardViewModel? model, bool throws)
        {
            _model = model;
            _throws = throws;
        }

        public static FakeDashboardQueries Succeeding(DashboardViewModel model) => new(model, throws: false);

        public static FakeDashboardQueries Throwing() => new(null, throws: true);

        public Task<DashboardViewModel> GetDashboardAsync(CancellationToken cancellationToken = default)
        {
            if (_throws)
            {
                throw new InvalidOperationException("Échec simulé d'assemblage du tableau de bord.");
            }

            return Task.FromResult(_model!);
        }
    }

    private sealed class StubChartRenderer : IChartRenderer
    {
        public Task InitializeAsync(IJSObjectReference jsModule, string containerId, ChartConfig config) => Task.CompletedTask;

        public Task UpdateAsync(IJSObjectReference jsModule, string containerId, ChartConfig config) => Task.CompletedTask;

        public Task DisposeAsync(IJSObjectReference jsModule, string containerId) => Task.CompletedTask;
    }
}
