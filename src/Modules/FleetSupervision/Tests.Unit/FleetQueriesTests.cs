namespace Liakont.Modules.FleetSupervision.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.FleetSupervision.Application;
using Liakont.Modules.FleetSupervision.Contracts;
using Liakont.Modules.FleetSupervision.Contracts.DTOs;
using Liakont.Modules.FleetSupervision.Domain;
using Liakont.Modules.FleetSupervision.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Vue d'ensemble du dashboard (OPS04) : <see cref="FleetQueries"/> liste le parc depuis le magasin puis
/// calcule les alertes avec les seuils et la dernière version publiée du central. Horloge fixe → déterministe.
/// </summary>
public sealed class FleetQueriesTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetOverview_Returns_Instances_Alerts_And_Latest_Version()
    {
        FleetInstanceDto healthy = Instance("ok", lastSeen: Now.AddMinutes(-5), version: "1.4.0");
        FleetInstanceDto mute = Instance("ko", lastSeen: Now.AddHours(-2), version: "1.4.0");
        var store = new FakeStore(healthy, mute);

        var options = Options.Create(new FleetSupervisionOptions
        {
            Central = new FleetCentralOptions
            {
                LatestVersion = "1.4.0",
                InstanceMuteThresholdMinutes = 30,
                BackupMaxAgeHours = 26,
            },
        });

        var queries = new FleetQueries(store, options, new FixedTimeProvider(Now));

        FleetOverviewDto overview = await queries.GetOverviewAsync(CancellationToken.None);

        overview.LatestVersion.Should().Be("1.4.0");
        overview.Instances.Should().HaveCount(2);
        overview.GeneratedUtc.Should().Be(Now);
        overview.Alerts.Should().ContainSingle(a => a.Kind == FleetAlertKind.InstanceMute && a.InstanceId == "ko");
    }

    private static FleetInstanceDto Instance(string id, DateTimeOffset lastSeen, string version) => new()
    {
        InstanceId = id,
        DisplayName = id,
        HostingMode = InstanceHostingMode.Operated,
        Version = version,
        HostHealth = InstanceHealthStatus.Healthy,
        DatabaseHealth = InstanceHealthStatus.Healthy,
        KeycloakHealth = InstanceHealthStatus.Healthy,
        TenantCount = 1,
        DiskFreeBytes = 1,
        DiskTotalBytes = 2,
        LastSuccessfulBackupUtc = Now.AddHours(-2),
        ContactEmail = null,
        FirstSeenUtc = Now.AddDays(-1),
        LastSeenUtc = lastSeen,
    };

    private sealed class FakeStore : IFleetInstanceStore
    {
        private readonly IReadOnlyList<FleetInstanceDto> _instances;

        public FakeStore(params FleetInstanceDto[] instances) => _instances = instances;

        public Task UpsertAsync(FleetInstance instance, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<FleetInstanceDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_instances);

        public Task<IReadOnlyList<FleetNotificationCandidate>> ListNotificationCandidatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FleetNotificationCandidate>>([]);

        public Task MarkNotifiedAsync(string instanceId, string notifiedVersion, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
