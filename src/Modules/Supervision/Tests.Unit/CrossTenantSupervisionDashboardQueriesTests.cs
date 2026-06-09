namespace Liakont.Modules.Supervision.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Liakont.Modules.Ingestion.Contracts.Queries;
using Liakont.Modules.Supervision.Contracts;
using Liakont.Modules.Supervision.Contracts.DTOs;
using Liakont.Modules.Supervision.Infrastructure;
using Liakont.Modules.Supervision.Tests.Unit.Doubles;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.MultiTenancy;
using Xunit;

/// <summary>
/// Tests de l'agrégateur cross-tenant du dashboard de supervision (SUP02) : il énumère les tenants (registre
/// système), ouvre un scope par tenant, agrège alertes + compteurs documents + état des agents, trie les
/// tenants à traiter en tête, garde VISIBLE un tenant injoignable (anti panne silencieuse), et route
/// l'acquittement dans le scope du bon tenant. Aucune base : le scope tenant est simulé.
/// </summary>
public sealed class CrossTenantSupervisionDashboardQueriesTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Overview_Aggregates_Active_Tenants_And_Sorts_Critical_First()
    {
        var alpha = TenantProvider(
            active: [Alert("Critical"), Alert("Warning")],
            counts: new() { ["Blocked"] = 4, ["RejectedByPa"] = 2, ["ReadyToSend"] = 7 },
            agents: [Agent("AG-1", lastSeen: Now.AddHours(-1))]);
        var beta = TenantProvider(
            active: [],
            counts: new() { ["ReadyToSend"] = 1 },
            agents: [Agent("AG-2", lastSeen: Now.AddHours(-3))]);

        var factory = new FakeTenantScopeFactory(new Dictionary<string, IServiceProvider>
        {
            ["alpha"] = alpha,
            ["beta"] = beta,
        });
        var tenants = new FakeTenantQueries(
            Tenant("beta", "Tenant Beta"),
            Tenant("alpha", "Tenant Alpha"),
            Tenant("gamma", "Tenant Gamma", active: false));

        var sut = NewSut(tenants, factory);

        var overview = await sut.GetInstanceOverviewAsync();

        overview.Should().HaveCount(2, "le tenant inactif est exclu");
        overview[0].TenantId.Should().Be("alpha", "un tenant avec une alerte critique passe en tête");
        overview[0].ActiveAlertCount.Should().Be(2);
        overview[0].CriticalAlertCount.Should().Be(1);
        overview[0].WorstSeverity.Should().Be("Critical");
        overview[0].BlockedDocumentCount.Should().Be(4);
        overview[0].RejectedByPaDocumentCount.Should().Be(2);
        overview[0].PendingDocumentCount.Should().Be(7);
        overview[0].LastAgentSeenUtc.Should().Be(Now.AddHours(-1));

        overview[1].TenantId.Should().Be("beta");
        overview[1].WorstSeverity.Should().BeNull();
    }

    [Fact]
    public async Task Overview_Keeps_A_Failed_Tenant_Visible_With_ReadFailed()
    {
        var ok = TenantProvider(active: [], counts: new(), agents: []);
        var factory = new FakeTenantScopeFactory(
            new Dictionary<string, IServiceProvider> { ["ok"] = ok, ["broken"] = ok },
            failing: new HashSet<string> { "broken" });
        var tenants = new FakeTenantQueries(Tenant("ok", "Tenant OK"), Tenant("broken", "Tenant Broken"));

        var sut = NewSut(tenants, factory);

        var overview = await sut.GetInstanceOverviewAsync();

        overview.Should().HaveCount(2, "un tenant injoignable n'est jamais masqué");
        overview.Single(r => r.TenantId == "broken").ReadFailed.Should().BeTrue();
        overview[0].TenantId.Should().Be("broken", "un tenant à traiter (lecture en échec) passe en tête");
    }

    [Fact]
    public async Task TenantDetail_Returns_Null_For_Unknown_Or_Inactive_Tenant()
    {
        var factory = new FakeTenantScopeFactory(new Dictionary<string, IServiceProvider>());
        var tenants = new FakeTenantQueries(Tenant("gamma", "Tenant Gamma", active: false));
        var sut = NewSut(tenants, factory);

        (await sut.GetTenantDetailAsync("inconnu")).Should().BeNull();
        (await sut.GetTenantDetailAsync("gamma")).Should().BeNull("un tenant inactif n'a pas de détail");
    }

    [Fact]
    public async Task TenantDetail_Aggregates_Agents_Alerts_And_Counts()
    {
        var provider = TenantProvider(
            active: [Alert("Critical")],
            recent: [Alert("Critical"), Alert("Warning", resolved: Now)],
            counts: new() { ["Blocked"] = 1 },
            agents: [Agent("AG-1", lastSeen: Now.AddHours(-1), version: "1.0.0")]);
        var factory = new FakeTenantScopeFactory(new Dictionary<string, IServiceProvider> { ["alpha"] = provider });
        var tenants = new FakeTenantQueries(Tenant("alpha", "Tenant Alpha"));
        var sut = NewSut(tenants, factory);

        var detail = await sut.GetTenantDetailAsync("alpha");

        detail.Should().NotBeNull();
        detail!.DisplayName.Should().Be("Tenant Alpha");
        detail.ActiveAlerts.Should().ContainSingle();
        detail.RecentAlerts.Should().HaveCount(2);
        detail.Agents.Should().ContainSingle();
        detail.Agents[0].Name.Should().Be("AG-1");
        detail.Agents[0].LastAgentVersion.Should().Be("1.0.0");
        detail.BlockedDocumentCount.Should().Be(1);
    }

    [Fact]
    public async Task Acknowledge_Routes_To_The_Tenant_Scoped_Service()
    {
        var ack = new RecordingAlertAcknowledgementService(result: true);
        var provider = TenantProvider(active: [], counts: new(), agents: [], acknowledgement: ack);
        var factory = new FakeTenantScopeFactory(new Dictionary<string, IServiceProvider> { ["alpha"] = provider });
        var tenants = new FakeTenantQueries(Tenant("alpha", "Tenant Alpha"));
        var sut = NewSut(tenants, factory);

        var alertId = Guid.NewGuid();
        var result = await sut.AcknowledgeAsync("alpha", alertId, "superviseur@instance");

        result.Should().BeTrue();
        ack.Calls.Should().ContainSingle();
        ack.Calls[0].Should().Be((alertId, "superviseur@instance"));
    }

    // ── Helpers ──

    private static CrossTenantSupervisionDashboardQueries NewSut(ITenantQueries tenants, ITenantScopeFactory factory) =>
        new(tenants, factory, NullLogger<CrossTenantSupervisionDashboardQueries>.Instance);

    private static MultiServiceProvider TenantProvider(
        IReadOnlyList<AlertDto> active,
        Dictionary<string, int> counts,
        IReadOnlyList<AgentSummaryDto> agents,
        IReadOnlyList<AlertDto>? recent = null,
        IAlertAcknowledgementService? acknowledgement = null)
    {
        var documents = new FakeDocumentQueries();
        documents.SetCountsByState(counts);

        return new MultiServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IAlertQueries)] = new FakeAlertQueries(active, recent),
            [typeof(IDocumentQueries)] = documents,
            [typeof(IAgentQueries)] = new FakeAgentQueries(agents.ToArray()),
            [typeof(IAlertAcknowledgementService)] = acknowledgement ?? new RecordingAlertAcknowledgementService(),
        });
    }

    private static TenantDto Tenant(string id, string displayName, bool active = true) => new()
    {
        Id = id,
        DisplayName = displayName,
        AdminEmail = $"admin@{id}",
        DatabaseName = $"db_{id}",
        IsActive = active,
        ProvisionedAt = Now,
    };

    private static AlertDto Alert(string severity, DateTimeOffset? resolved = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = "alpha",
        RuleKey = "agent.mute",
        Severity = severity,
        Detail = "détail opérateur",
        TriggeredUtc = Now.AddHours(-2),
        ResolvedUtc = resolved,
    };

    private static AgentSummaryDto Agent(string name, DateTimeOffset? lastSeen, string? version = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        KeyPrefix = "LIA_pub",
        IsRevoked = false,
        CreatedAt = Now.AddDays(-10),
        LastSeenAtUtc = lastSeen,
        LastAgentVersion = version,
    };
}
