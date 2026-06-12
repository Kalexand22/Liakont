namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Modules.FleetSupervision.Contracts;
using Liakont.Modules.FleetSupervision.Contracts.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Tests bUnit du dashboard de flotte (OPS04, page <c>/flotte</c>) : rendu des instances (libellés FR de
/// santé / hébergement), section d'alertes (gravité FR), états vides, et bandeau d'erreur si la lecture
/// échoue (anti faux-vert). La garde de permission (route <c>[Authorize(Policy = liakont.fleet)]</c>) est
/// vérifiée côté nav (LiakontNavSectionProviderTests).
/// </summary>
public sealed class FlotteTests : BunitContext
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    public FlotteTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
    }

    [Fact]
    public void Renders_Instances_With_French_Labels()
    {
        var overview = new FleetOverviewDto
        {
            LatestVersion = "1.4.0",
            GeneratedUtc = Now,
            Instances =
            [
                Instance("azmut-1", "AZMUT 1", InstanceHostingMode.SelfHosted, "1.4.0", InstanceHealthStatus.Healthy),
            ],
            Alerts = [],
        };
        Services.AddScoped<IFleetQueries>(_ => new FakeFleetQueries(overview));

        var cut = Render<Flotte>();

        cut.FindAll("[data-testid='flotte-intro']").Should().ContainSingle();
        cut.FindAll("[data-testid='flotte-instances']").Should().ContainSingle();
        cut.FindAll("[data-testid='flotte-instance-row']").Should().ContainSingle();
        cut.Markup.Should().Contain("AZMUT 1").And.Contain("Self-hosted").And.Contain("Sain");
        cut.FindAll("[data-testid='flotte-alerts-empty']").Should().ContainSingle();
    }

    [Fact]
    public void Renders_Alerts_With_French_Severity()
    {
        var overview = new FleetOverviewDto
        {
            LatestVersion = "1.4.0",
            GeneratedUtc = Now,
            Instances = [Instance("ko", "Instance KO", InstanceHostingMode.Operated, "1.0.0", InstanceHealthStatus.Unhealthy)],
            Alerts =
            [
                new FleetAlertDto
                {
                    InstanceId = "ko",
                    DisplayName = "Instance KO",
                    Kind = FleetAlertKind.InstanceMute,
                    Severity = FleetAlertSeverity.Critical,
                    Message = "Instance muette : aucun signe de vie depuis 2 h.",
                },
            ],
        };
        Services.AddScoped<IFleetQueries>(_ => new FakeFleetQueries(overview));

        var cut = Render<Flotte>();

        cut.FindAll("[data-testid='flotte-alert-row']").Should().ContainSingle();
        cut.Markup.Should().Contain("Critique").And.Contain("Instance muette");
        cut.FindAll("[data-testid='flotte-alerts-empty']").Should().BeEmpty();
    }

    [Fact]
    public void Shows_Empty_State_When_No_Instance()
    {
        var overview = new FleetOverviewDto { LatestVersion = "1.4.0", GeneratedUtc = Now, Instances = [], Alerts = [] };
        Services.AddScoped<IFleetQueries>(_ => new FakeFleetQueries(overview));

        var cut = Render<Flotte>();

        cut.FindAll("[data-testid='flotte-empty']").Should().ContainSingle();
    }

    [Fact]
    public void Shows_Error_Banner_When_Read_Throws()
    {
        Services.AddScoped<IFleetQueries>(_ => new ThrowingFleetQueries());

        var cut = Render<Flotte>();

        // L'échec de lecture reste VISIBLE (bandeau) et n'expose pas une liste trompeuse (anti faux-vert).
        cut.FindAll("[data-testid='flotte-error']").Should().ContainSingle();
        cut.FindAll("[data-testid='flotte-instances']").Should().BeEmpty();
    }

    private static FleetInstanceDto Instance(string id, string displayName, InstanceHostingMode mode, string version, InstanceHealthStatus health) => new()
    {
        InstanceId = id,
        DisplayName = displayName,
        HostingMode = mode,
        Version = version,
        HostHealth = health,
        DatabaseHealth = health,
        KeycloakHealth = InstanceHealthStatus.Unknown,
        TenantCount = 2,
        DiskFreeBytes = 50_000_000_000,
        DiskTotalBytes = 100_000_000_000,
        LastSuccessfulBackupUtc = Now.AddHours(-2),
        ContactEmail = null,
        FirstSeenUtc = Now.AddDays(-5),
        LastSeenUtc = Now.AddMinutes(-5),
    };

    private sealed class FakeFleetQueries : IFleetQueries
    {
        private readonly FleetOverviewDto _overview;

        public FakeFleetQueries(FleetOverviewDto overview) => _overview = overview;

        public Task<FleetOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_overview);
    }

    private sealed class ThrowingFleetQueries : IFleetQueries
    {
        public Task<FleetOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("lecture indisponible");
    }
}
