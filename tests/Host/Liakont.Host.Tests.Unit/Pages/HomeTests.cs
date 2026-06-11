namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.Dashboard;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class HomeTests : BunitContext
{
    public HomeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
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

    private static DashboardViewModel BuildModel() => new()
    {
        ProfileConfigured = true,
        StateCounts = [new DashboardStateCount("Detected", 0)],
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
}
