namespace Liakont.Modules.FleetSupervision.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Linq;
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
/// Round-trip réel du magasin de flotte (OPS04) sur PostgreSQL : upsert idempotent (premier-vu préservé,
/// télémétrie mise à jour), candidats de notification (self-hosted joignables), mémorisation de version
/// notifiée, et calcul d'alertes de bout en bout via <see cref="FleetQueries"/>.
/// </summary>
[Collection("FleetIntegration")]
public sealed class PostgresFleetStoreIntegrationTests : IClassFixture<FleetDatabaseFixture>
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 12, 8, 0, 0, TimeSpan.Zero);

    private readonly FleetDatabaseFixture _fixture;

    public PostgresFleetStoreIntegrationTests(FleetDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Upsert_Then_List_RoundTrips_All_Fields()
    {
        PostgresFleetStore store = NewStore();
        var report = Report("rt-1", InstanceHostingMode.Operated, version: "1.4.0", tenantCount: 4);

        await store.UpsertAsync(FleetInstance.Register(report, T0), CancellationToken.None);

        IReadOnlyList<FleetInstanceDto> all = await store.ListAsync(CancellationToken.None);
        FleetInstanceDto? row = all.FirstOrDefault(i => i.InstanceId == "rt-1");

        row.Should().NotBeNull();
        row!.Version.Should().Be("1.4.0");
        row.TenantCount.Should().Be(4);
        row.HostHealth.Should().Be(InstanceHealthStatus.Healthy);
        row.FirstSeenUtc.Should().Be(T0);
        row.LastSeenUtc.Should().Be(T0);
    }

    [Fact]
    public async Task Upsert_Preserves_FirstSeen_And_Updates_LastSeen()
    {
        PostgresFleetStore store = NewStore();
        DateTimeOffset later = T0.AddHours(6);

        await store.UpsertAsync(FleetInstance.Register(Report("rt-2", version: "1.3.0"), T0), CancellationToken.None);
        await store.UpsertAsync(FleetInstance.Register(Report("rt-2", version: "1.4.0"), later), CancellationToken.None);

        FleetInstanceDto row = (await store.ListAsync(CancellationToken.None)).Single(i => i.InstanceId == "rt-2");

        row.FirstSeenUtc.Should().Be(T0, "le premier signe de vie est préservé par l'upsert");
        row.LastSeenUtc.Should().Be(later, "le dernier signe de vie est mis à jour");
        row.Version.Should().Be("1.4.0", "la télémétrie est rafraîchie");
    }

    [Fact]
    public async Task NotificationCandidates_Are_SelfHosted_With_Email_And_MarkNotified_Persists()
    {
        PostgresFleetStore store = NewStore();
        await store.UpsertAsync(FleetInstance.Register(Report("op-1", InstanceHostingMode.Operated, contactEmail: "ops@x.example"), T0), CancellationToken.None);
        await store.UpsertAsync(FleetInstance.Register(Report("sh-no-mail", InstanceHostingMode.SelfHosted), T0), CancellationToken.None);
        await store.UpsertAsync(FleetInstance.Register(Report("sh-1", InstanceHostingMode.SelfHosted, contactEmail: "it@editeur.example"), T0), CancellationToken.None);

        IReadOnlyList<FleetNotificationCandidate> candidates = await store.ListNotificationCandidatesAsync(CancellationToken.None);

        candidates.Select(c => c.InstanceId).Should().BeEquivalentTo(["sh-1"]);
        candidates[0].NotifiedVersion.Should().BeNull();

        await store.MarkNotifiedAsync("sh-1", "1.4.0", CancellationToken.None);

        candidates = await store.ListNotificationCandidatesAsync(CancellationToken.None);
        candidates.Single().NotifiedVersion.Should().Be("1.4.0");
    }

    [Fact]
    public async Task FleetQueries_Computes_Alerts_Over_The_Real_Store()
    {
        PostgresFleetStore store = NewStore();
        DateTimeOffset now = T0.AddDays(1);

        // Instance muette (dernier signe de vie il y a 2 h) + version obsolète.
        await store.UpsertAsync(FleetInstance.Register(Report("alert-1", version: "1.0.0"), now.AddHours(-2)), CancellationToken.None);

        var options = Options.Create(new FleetSupervisionOptions
        {
            Central = new FleetCentralOptions
            {
                LatestVersion = "1.4.0",
                InstanceMuteThresholdMinutes = 30,
                BackupMaxAgeHours = 26,
            },
        });
        var queries = new FleetQueries(store, options, new FixedTimeProvider(now));

        FleetOverviewDto overview = await queries.GetOverviewAsync(CancellationToken.None);

        overview.LatestVersion.Should().Be("1.4.0");
        overview.Alerts.Should().Contain(a => a.InstanceId == "alert-1" && a.Kind == FleetAlertKind.InstanceMute);
        overview.Alerts.Should().Contain(a => a.InstanceId == "alert-1" && a.Kind == FleetAlertKind.ObsoleteVersion);
    }

    private static InstanceHeartbeatReport Report(
        string instanceId,
        InstanceHostingMode hostingMode = InstanceHostingMode.Operated,
        string version = "1.4.0",
        int tenantCount = 1,
        string? contactEmail = null) => new()
    {
        InstanceId = instanceId,
        DisplayName = instanceId,
        HostingMode = hostingMode,
        Version = version,
        HostHealth = InstanceHealthStatus.Healthy,
        DatabaseHealth = InstanceHealthStatus.Healthy,
        KeycloakHealth = InstanceHealthStatus.Unknown,
        TenantCount = tenantCount,
        DiskFreeBytes = 10_000_000_000,
        DiskTotalBytes = 100_000_000_000,
        LastSuccessfulBackupUtc = T0.AddHours(-1),
        ContactEmail = contactEmail,
        SentAtUtc = T0,
    };

    private PostgresFleetStore NewStore() => new(_fixture.CreateConnectionFactory());

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
