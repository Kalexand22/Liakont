namespace Liakont.Host.Tests.Unit.Components;

using System;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Liakont.Host.Supervision;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Témoin de vie de la supervision (FIX210, F12 §5.1) : « jamais évaluée » et « en retard » sont des alertes
/// VISIBLES (role=alert), « saine » et « indéterminée » de simples statuts. Une supervision muette n'est plus
/// indiscernable d'une absence d'alerte.
/// </summary>
public sealed class SupervisionLivenessBannerTests : BunitContext
{
    public SupervisionLivenessBannerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddCommonUI();
    }

    [Fact]
    public void Never_Evaluated_Renders_A_Visible_Alert()
    {
        var cut = RenderWith(SupervisionLivenessStatus.NeverEvaluated, last: null);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='supervision-liveness-never']").Should().ContainSingle();
            cut.Find("[data-testid='supervision-liveness']").GetAttribute("role").Should().Be("alert");
        });
    }

    [Fact]
    public void Overdue_Renders_A_Visible_Alert_With_The_Last_Evaluation()
    {
        var last = new DateTimeOffset(2026, 6, 11, 9, 0, 0, TimeSpan.Zero);
        var cut = RenderWith(SupervisionLivenessStatus.Overdue, last);

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid='supervision-liveness-overdue']");
            banner.TextContent.Should().Contain("11/06/2026 09:00 UTC");
            cut.Find("[data-testid='supervision-liveness']").GetAttribute("role").Should().Be("alert");
        });
    }

    [Fact]
    public void Healthy_Renders_A_Status_Not_An_Alert()
    {
        var last = new DateTimeOffset(2026, 6, 11, 11, 50, 0, TimeSpan.Zero);
        var cut = RenderWith(SupervisionLivenessStatus.Healthy, last);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='supervision-liveness-healthy']").Should().ContainSingle();
            cut.Find("[data-testid='supervision-liveness']").GetAttribute("role").Should().Be("status");
        });
    }

    [Fact]
    public void Unknown_Renders_Without_A_False_Alert()
    {
        var cut = RenderWith(SupervisionLivenessStatus.Unknown, last: null);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='supervision-liveness-unknown']").Should().ContainSingle();
            cut.Find("[data-testid='supervision-liveness']").GetAttribute("role").Should().Be("status");
        });
    }

    private IRenderedComponent<SupervisionLivenessBanner> RenderWith(SupervisionLivenessStatus status, DateTimeOffset? last)
    {
        Services.AddScoped<ISupervisionLivenessProvider>(_ => new FakeLivenessProvider(new SupervisionLivenessView
        {
            LastEvaluationUtc = last,
            Status = status,
            IntervalMinutes = 15,
        }));

        return Render<SupervisionLivenessBanner>();
    }

    private sealed class FakeLivenessProvider : ISupervisionLivenessProvider
    {
        private readonly SupervisionLivenessView _view;

        public FakeLivenessProvider(SupervisionLivenessView view) => _view = view;

        public Task<SupervisionLivenessView> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_view);
    }
}
