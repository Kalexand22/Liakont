namespace Liakont.Host.Tests.Unit.Dashboard;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Dashboard;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Liakont.Modules.Ingestion.Contracts.Queries;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Stratum.Common.Abstractions.MultiTenancy;
using Xunit;

public sealed class DashboardQueryServiceTests
{
    // Horloge FIXE des tests : 12 juin 2026 → mois en cours = juin, mois précédent = mai, année = 2026.
    private static readonly DateTimeOffset TestNow = new(2026, 6, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetDashboardAsync_Should_Order_Counts_Canonically_And_Default_To_Zero()
    {
        var service = BuildService(counts: new Dictionary<string, int> { ["Issued"] = 7 });

        var model = await service.GetDashboardAsync();

        // L'ordre canonique commence par "Detected" et contient les états clés à 0 par défaut.
        model.CurrentMonth.Counts[0].State.Should().Be("Detected");
        model.CurrentMonth.Counts.Single(c => c.State == "Detected").Count.Should().Be(0);
        model.CurrentMonth.Counts.Single(c => c.State == "Issued").Count.Should().Be(7);
        model.CurrentMonth.Counts.Select(c => c.State).Should().Contain(["Detected", "Blocked", "Issued", "RejectedByPa"]);
    }

    [Fact]
    public async Task GetDashboardAsync_Should_Append_Unmapped_States_Without_Losing_Them()
    {
        var service = BuildService(counts: new Dictionary<string, int> { ["SomeFutureState"] = 4 });

        var model = await service.GetDashboardAsync();

        model.CurrentMonth.Counts.Single(c => c.State == "SomeFutureState").Count.Should().Be(4);
    }

    [Fact]
    public async Task GetDashboardAsync_Should_Compute_The_Three_Scope_Windows_From_The_Clock()
    {
        // Les bornes portées par le modèle pilotent les liens de drill-down : elles doivent
        // correspondre exactement aux fenêtres comptées (12 juin 2026 → juin / mai / 2026).
        var service = BuildService();

        var model = await service.GetDashboardAsync();

        model.CurrentMonth.Key.Should().Be("current-month");
        model.CurrentMonth.From.Should().Be(new DateOnly(2026, 6, 1));
        model.CurrentMonth.To.Should().Be(new DateOnly(2026, 6, 30));
        model.PreviousMonth.Key.Should().Be("previous-month");
        model.PreviousMonth.From.Should().Be(new DateOnly(2026, 5, 1));
        model.PreviousMonth.To.Should().Be(new DateOnly(2026, 5, 31));
        model.CurrentYear.Key.Should().Be("current-year");
        model.CurrentYear.From.Should().Be(new DateOnly(2026, 1, 1));
        model.CurrentYear.To.Should().Be(new DateOnly(2026, 12, 31));
    }

    [Fact]
    public async Task GetDashboardAsync_Should_Query_Counts_With_Each_Scope_Bounds()
    {
        // Anti-faux-vert : les compteurs de chaque périmètre doivent être CALCULÉS sur ses bornes
        // (filtre From/To passé au module), pas réutilisés d'une requête globale.
        var documentQueries = new FakeDocumentQueries(new Dictionary<string, int>());
        var service = BuildService(documentQueries: documentQueries);

        await service.GetDashboardAsync();

        documentQueries.ReceivedFilters.Should().HaveCount(3);
        documentQueries.ReceivedFilters[0].From.Should().Be(new DateOnly(2026, 6, 1));
        documentQueries.ReceivedFilters[0].To.Should().Be(new DateOnly(2026, 6, 30));
        documentQueries.ReceivedFilters[1].From.Should().Be(new DateOnly(2026, 5, 1));
        documentQueries.ReceivedFilters[1].To.Should().Be(new DateOnly(2026, 5, 31));
        documentQueries.ReceivedFilters[2].From.Should().Be(new DateOnly(2026, 1, 1));
        documentQueries.ReceivedFilters[2].To.Should().Be(new DateOnly(2026, 12, 31));
    }

    [Fact]
    public async Task GetDashboardAsync_Should_Report_Distinct_Counts_Per_Scope()
    {
        var documentQueries = new FakeDocumentQueries(
            new Dictionary<string, int>(),
            countsByWindow: new Dictionary<(DateOnly From, DateOnly To), IReadOnlyDictionary<string, int>>
            {
                [(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30))] = new Dictionary<string, int> { ["Issued"] = 4 },
                [(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31))] = new Dictionary<string, int> { ["Issued"] = 9 },
                [(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))] = new Dictionary<string, int> { ["Issued"] = 13 },
            });
        var service = BuildService(documentQueries: documentQueries);

        var model = await service.GetDashboardAsync();

        model.CurrentMonth.Counts.Single(c => c.State == "Issued").Count.Should().Be(4);
        model.PreviousMonth.Counts.Single(c => c.State == "Issued").Count.Should().Be(9);
        model.CurrentYear.Counts.Single(c => c.State == "Issued").Count.Should().Be(13);
    }

    [Fact]
    public async Task GetDashboardAsync_Should_Report_Tva_Not_Configured_When_Absent()
    {
        var service = BuildService(tva: null);

        var model = await service.GetDashboardAsync();

        model.TvaStatus.Should().Be(DashboardTvaStatus.NotConfigured);
    }

    [Theory]
    [InlineData(true, DashboardTvaStatus.Validated)]
    [InlineData(false, DashboardTvaStatus.NotValidated)]
    public async Task GetDashboardAsync_Should_Derive_Tva_Status_From_Validation(bool isValidated, DashboardTvaStatus expected)
    {
        var tva = new TvaMappingSummaryDto
        {
            MappingVersion = "v1",
            IsValidated = isValidated,
            ValidatedBy = isValidated ? "Cabinet" : null,
            ValidatedDate = isValidated ? new DateOnly(2026, 6, 1) : null,
            DefaultBehavior = "Block",
            RuleCount = 3,
        };
        var service = BuildService(tva: tva);

        var model = await service.GetDashboardAsync();

        model.TvaStatus.Should().Be(expected);
        if (isValidated)
        {
            model.TvaValidatedBy.Should().Be("Cabinet");
            model.TvaValidatedDate.Should().Be(new DateOnly(2026, 6, 1));
        }
    }

    [Theory]
    [InlineData("Mensuelle", "Mensuelle")]
    [InlineData(null, null)]
    public async Task GetDashboardAsync_Should_Pass_Through_Reporting_Frequency_Without_Computing(string? frequency, string? expected)
    {
        var fiscal = new FiscalSettingsDto
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            ReportingFrequency = frequency,
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
        var service = BuildService(fiscal: fiscal);

        var model = await service.GetDashboardAsync();

        model.ReportingFrequency.Should().Be(expected);
    }

    [Fact]
    public async Task GetDashboardAsync_Should_Map_Agents_For_The_Resolved_Tenant()
    {
        var agent = new AgentSummaryDto
        {
            Id = Guid.NewGuid(),
            Name = "Agent A",
            KeyPrefix = "abc",
            IsRevoked = false,
            CreatedAt = DateTimeOffset.UnixEpoch,
            LastSeenAtUtc = new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero),
            LastAgentVersion = "1.2.3",
        };
        var service = BuildService(tenantId: "tenant-a", agents: [agent]);

        var model = await service.GetDashboardAsync();

        model.Agents.Should().ContainSingle();
        model.Agents[0].Name.Should().Be("Agent A");
        model.Agents[0].Version.Should().Be("1.2.3");
        model.Agents[0].IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task GetDashboardAsync_Should_Return_No_Agents_When_Tenant_Unresolved()
    {
        var agent = new AgentSummaryDto
        {
            Id = Guid.NewGuid(),
            Name = "Agent A",
            KeyPrefix = "abc",
            IsRevoked = false,
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
        var service = BuildService(tenantId: null, agents: [agent]);

        var model = await service.GetDashboardAsync();

        model.Agents.Should().BeEmpty("sans tenant résolu, aucune lecture cross-tenant n'est tentée");
    }

    private static DashboardQueryService BuildService(
        IReadOnlyDictionary<string, int>? counts = null,
        IReadOnlyList<AgentSummaryDto>? agents = null,
        TvaMappingSummaryDto? tva = null,
        FiscalSettingsDto? fiscal = null,
        string? tenantId = "tenant-a",
        FakeDocumentQueries? documentQueries = null)
    {
        var overview = new TenantSettingsOverviewDto
        {
            Profile = null,
            FiscalSettings = fiscal,
            TvaMapping = tva,
            PaAccounts = [],
        };

        return new DashboardQueryService(
            documentQueries ?? new FakeDocumentQueries(counts ?? new Dictionary<string, int>()),
            new FakeAgentQueries(agents ?? []),
            new FakeSettingsQueries(overview),
            new FakeTenantContext(tenantId),
            new FixedTimeProvider(TestNow));
    }

    /// <summary>Horloge figée (fuseau UTC) : les fenêtres calculées par le service sont déterministes.</summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FakeDocumentQueries : IDocumentStateCountQueries
    {
        private readonly IReadOnlyDictionary<string, int> _counts;
        private readonly IReadOnlyDictionary<(DateOnly From, DateOnly To), IReadOnlyDictionary<string, int>>? _countsByWindow;

        public FakeDocumentQueries(
            IReadOnlyDictionary<string, int> counts,
            IReadOnlyDictionary<(DateOnly From, DateOnly To), IReadOnlyDictionary<string, int>>? countsByWindow = null)
        {
            _counts = counts;
            _countsByWindow = countsByWindow;
        }

        public List<DocumentListFilter> ReceivedFilters { get; } = [];

        public Task<IReadOnlyDictionary<string, int>> GetStateCountsAsync(DocumentListFilter filter, CancellationToken cancellationToken = default)
        {
            ReceivedFilters.Add(filter);

            var counts = _countsByWindow is not null
                && filter.From is { } from
                && filter.To is { } to
                && _countsByWindow.TryGetValue((from, to), out var windowCounts)
                ? windowCounts
                : _counts;

            return Task.FromResult(counts);
        }
    }

    private sealed class FakeAgentQueries : IAgentQueries
    {
        private readonly IReadOnlyList<AgentSummaryDto> _agents;

        public FakeAgentQueries(IReadOnlyList<AgentSummaryDto> agents) => _agents = agents;

        public Task<IReadOnlyList<AgentSummaryDto>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_agents);
    }

    private sealed class FakeSettingsQueries : ITenantSettingsConsoleQueries
    {
        private readonly TenantSettingsOverviewDto _overview;

        public FakeSettingsQueries(TenantSettingsOverviewDto overview) => _overview = overview;

        public Task<TenantSettingsOverviewDto> GetSettingsOverview(CancellationToken ct = default) => Task.FromResult(_overview);
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public FakeTenantContext(string? tenantId) => TenantId = tenantId;

        public string? TenantId { get; }

        public bool IsResolved => TenantId is not null;
    }
}
