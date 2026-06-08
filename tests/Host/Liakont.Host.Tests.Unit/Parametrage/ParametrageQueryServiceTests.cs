namespace Liakont.Host.Tests.Unit.Parametrage;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Parametrage;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Liakont.Modules.Ingestion.Contracts.Queries;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Stratum.Common.Abstractions.MultiTenancy;
using Xunit;

public sealed class ParametrageQueryServiceTests
{
    [Fact]
    public async Task GetParametrageAsync_Should_Return_Empty_Agents_And_Not_Call_AgentQueries_When_Tenant_Unresolved()
    {
        var dummyAgent = new AgentSummaryDto
        {
            Id = Guid.NewGuid(),
            Name = "Agent A",
            KeyPrefix = "abc",
            IsRevoked = false,
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
        var agentQueries = new FakeAgentQueries([dummyAgent]);
        var service = BuildService(tenantId: null, agentQueries: agentQueries);

        var model = await service.GetParametrageAsync();

        model.Agents.Should().BeEmpty("sans tenant résolu, aucune lecture cross-tenant n'est tentée");
        agentQueries.ListByTenantCallCount.Should().Be(0, "ListByTenantAsync ne doit pas être appelé si tenantId est vide");
    }

    [Fact]
    public async Task GetParametrageAsync_Should_Map_Agents_For_Resolved_Tenant_In_Order()
    {
        var firstSeen = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
        var secondSeen = new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

        var agents = new List<AgentSummaryDto>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Agent Alpha",
                KeyPrefix = "aaa",
                IsRevoked = false,
                CreatedAt = DateTimeOffset.UnixEpoch,
                LastSeenAtUtc = firstSeen,
                LastAgentVersion = "1.0.0",
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Agent Beta",
                KeyPrefix = "bbb",
                IsRevoked = true,
                CreatedAt = DateTimeOffset.UnixEpoch,
                LastSeenAtUtc = secondSeen,
                LastAgentVersion = "2.3.1",
            },
        };
        var service = BuildService(tenantId: "tenant-a", agents: agents);

        var model = await service.GetParametrageAsync();

        model.Agents.Should().HaveCount(2);
        model.Agents[0].Name.Should().Be("Agent Alpha");
        model.Agents[0].LastSeenUtc.Should().Be(firstSeen);
        model.Agents[0].Version.Should().Be("1.0.0");
        model.Agents[0].IsRevoked.Should().BeFalse();
        model.Agents[1].Name.Should().Be("Agent Beta");
        model.Agents[1].LastSeenUtc.Should().Be(secondSeen);
        model.Agents[1].Version.Should().Be("2.3.1");
        model.Agents[1].IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task GetParametrageAsync_Should_Pass_Through_Settings_Overview_Fields_Faithfully()
    {
        var profile = new TenantProfileDto
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            Siren = "123456789",
            RaisonSociale = "Dupont SARL",
            Street = "1 rue de la Paix",
            PostalCode = "75001",
            City = "Paris",
            Country = "FR",
            Statut = "Actif",
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
        var fiscal = new FiscalSettingsDto
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            ReportingFrequency = "Mensuelle",
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
        var tva = new TvaMappingSummaryDto
        {
            MappingVersion = "v2",
            IsValidated = true,
            ValidatedBy = "Cabinet",
            ValidatedDate = new DateOnly(2026, 5, 1),
            DefaultBehavior = "Block",
            RuleCount = 5,
        };
        var paAccounts = new List<PaAccountSettingsDto>();
        var service = BuildService(
            tenantId: "tenant-a",
            profile: profile,
            fiscal: fiscal,
            tva: tva,
            paAccounts: paAccounts);

        var model = await service.GetParametrageAsync();

        model.Profile.Should().Be(profile);
        model.FiscalSettings.Should().Be(fiscal);
        model.TvaMapping.Should().Be(tva);
        model.PaAccounts.Should().BeSameAs(paAccounts);
    }

    [Fact]
    public async Task VerifyArchiveIntegrityAsync_Should_Delegate_To_IArchiveVerifier()
    {
        var expectedReport = new ArchiveVerificationReport(
            new ArchiveIntegrityReport(IsIntact: true, EntryCount: 2, Entries: [], FirstBreakDetail: null),
            Anchors: [],
            IsChainAnchored: true,
            IsFullyVerified: true,
            Summary: "Coffre intègre.");
        var service = BuildService(archiveReport: expectedReport);

        var report = await service.VerifyArchiveIntegrityAsync();

        report.Should().Be(expectedReport);
    }

    private static ParametrageQueryService BuildService(
        IReadOnlyList<AgentSummaryDto>? agents = null,
        FakeAgentQueries? agentQueries = null,
        TenantProfileDto? profile = null,
        FiscalSettingsDto? fiscal = null,
        TvaMappingSummaryDto? tva = null,
        IReadOnlyList<PaAccountSettingsDto>? paAccounts = null,
        ArchiveVerificationReport? archiveReport = null,
        string? tenantId = "tenant-a")
    {
        var overview = new TenantSettingsOverviewDto
        {
            Profile = profile,
            FiscalSettings = fiscal,
            TvaMapping = tva,
            PaAccounts = paAccounts ?? [],
        };

        return new ParametrageQueryService(
            new FakeSettingsQueries(overview),
            agentQueries ?? new FakeAgentQueries(agents ?? []),
            new FakeArchiveVerifier(archiveReport),
            new FakeTenantContext(tenantId));
    }

    private sealed class FakeAgentQueries : IAgentQueries
    {
        private readonly IReadOnlyList<AgentSummaryDto> _agents;

        public FakeAgentQueries(IReadOnlyList<AgentSummaryDto> agents) => _agents = agents;

        public int ListByTenantCallCount { get; private set; }

        public Task<IReadOnlyList<AgentSummaryDto>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            ListByTenantCallCount++;
            return Task.FromResult(_agents);
        }
    }

    private sealed class FakeSettingsQueries : ITenantSettingsConsoleQueries
    {
        private readonly TenantSettingsOverviewDto _overview;

        public FakeSettingsQueries(TenantSettingsOverviewDto overview) => _overview = overview;

        public Task<TenantSettingsOverviewDto> GetSettingsOverview(CancellationToken ct = default) => Task.FromResult(_overview);
    }

    private sealed class FakeArchiveVerifier : IArchiveVerifier
    {
        private readonly ArchiveVerificationReport? _report;

        public FakeArchiveVerifier(ArchiveVerificationReport? report) => _report = report;

        public Task<ArchiveVerificationReport> VerifyTenantVaultAsync(CancellationToken cancellationToken = default)
        {
            if (_report is null)
            {
                throw new NotSupportedException("No report configured for this fake.");
            }

            return Task.FromResult(_report);
        }
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public FakeTenantContext(string? tenantId) => TenantId = tenantId;

        public string? TenantId { get; }

        public bool IsResolved => TenantId is not null;
    }
}
