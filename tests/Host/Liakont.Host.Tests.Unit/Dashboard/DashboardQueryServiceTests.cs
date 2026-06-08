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
    [Fact]
    public async Task GetDashboardAsync_Should_Order_Counts_Canonically_And_Default_To_Zero()
    {
        var service = BuildService(counts: new Dictionary<string, int> { ["Issued"] = 7 });

        var model = await service.GetDashboardAsync();

        // L'ordre canonique commence par "Detected" et contient les états clés à 0 par défaut.
        model.StateCounts[0].State.Should().Be("Detected");
        model.StateCounts.Single(c => c.State == "Detected").Count.Should().Be(0);
        model.StateCounts.Single(c => c.State == "Issued").Count.Should().Be(7);
        model.StateCounts.Select(c => c.State).Should().Contain(["Detected", "Blocked", "Issued", "RejectedByPa"]);
    }

    [Fact]
    public async Task GetDashboardAsync_Should_Append_Unmapped_States_Without_Losing_Them()
    {
        var service = BuildService(counts: new Dictionary<string, int> { ["SomeFutureState"] = 4 });

        var model = await service.GetDashboardAsync();

        model.StateCounts.Single(c => c.State == "SomeFutureState").Count.Should().Be(4);
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
        string? tenantId = "tenant-a")
    {
        var overview = new TenantSettingsOverviewDto
        {
            Profile = null,
            FiscalSettings = fiscal,
            TvaMapping = tva,
            PaAccounts = [],
        };

        return new DashboardQueryService(
            new FakeDocumentQueries(counts ?? new Dictionary<string, int>()),
            new FakeAgentQueries(agents ?? []),
            new FakeSettingsQueries(overview),
            new FakeTenantContext(tenantId));
    }

    private sealed class FakeDocumentQueries : IDocumentQueries
    {
        private readonly IReadOnlyDictionary<string, int> _counts;

        public FakeDocumentQueries(IReadOnlyDictionary<string, int> counts) => _counts = counts;

        public Task<DocumentListResult> GetDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DocumentListResult
            {
                Items = [],
                Page = filter.Page,
                PageSize = filter.PageSize,
                TotalCount = 0,
                CountsByState = _counts,
            });

        public Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentDto?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetByStateAsync(string state, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ArchiveReferenceDto?> GetArchiveReferenceAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetPotentiallySentDocumentsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentStatusDto?> FindStatusBySourceReferenceAndPayloadHashAsync(string sourceReference, string payloadHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
