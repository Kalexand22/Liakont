namespace Liakont.Modules.Supervision.Tests.Unit.Rules;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Application.Rules;
using Liakont.Modules.Supervision.Domain;
using Liakont.Modules.Supervision.Tests.Unit.Doubles;
using Xunit;

/// <summary>
/// Règle « Agent muet » (SUP01b, F12 §5.2) : seuil par défaut 24 h, surcharge tenant (CFG02), exclusion des
/// agents révoqués, silence d'un agent jamais vu mesuré depuis son enregistrement. (INV-SUPERVISION-010)
/// </summary>
public sealed class AgentMuteAlertRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static AlertEvaluationContext Context => new("acme", Now);

    [Fact]
    public void Rule_Key_And_Severity_Are_Stable()
    {
        var rule = new AgentMuteAlertRule(new FakeAgentQueries(), new FakeTenantSettingsQueries());

        rule.RuleKey.Should().Be("agent.mute");
        rule.Severity.Should().Be(AlertSeverity.Critical);
    }

    [Fact]
    public async Task Fires_When_Active_Agent_Silent_Beyond_Default_Threshold()
    {
        var agents = new FakeAgentQueries(
            RuleAlertTestData.Agent("Poste de vente", lastSeenAtUtc: Now.AddHours(-25), createdAt: Now.AddDays(-10)));
        var rule = new AgentMuteAlertRule(agents, new FakeTenantSettingsQueries());

        var evaluation = await rule.EvaluateAsync(Context);

        evaluation.IsFiring.Should().BeTrue();
        evaluation.Detail.Should().Contain("Poste de vente").And.Contain("Liakont Agent");
    }

    [Fact]
    public async Task Does_Not_Fire_When_Within_Threshold()
    {
        var agents = new FakeAgentQueries(
            RuleAlertTestData.Agent("Poste", lastSeenAtUtc: Now.AddHours(-23), createdAt: Now.AddDays(-10)));
        var rule = new AgentMuteAlertRule(agents, new FakeTenantSettingsQueries());

        (await rule.EvaluateAsync(Context)).IsFiring.Should().BeFalse();
    }

    [Fact]
    public async Task Boundary_Exactly_At_Threshold_Does_Not_Fire()
    {
        // « > 24 h » strict : un dernier contact pile à 24 h n'a pas encore dépassé le seuil.
        var agents = new FakeAgentQueries(
            RuleAlertTestData.Agent("Poste", lastSeenAtUtc: Now.AddHours(-24), createdAt: Now.AddDays(-10)));
        var rule = new AgentMuteAlertRule(agents, new FakeTenantSettingsQueries());

        (await rule.EvaluateAsync(Context)).IsFiring.Should().BeFalse();
    }

    [Fact]
    public async Task Never_Seen_Agent_Counts_Silence_From_Registration()
    {
        var agents = new FakeAgentQueries(
            RuleAlertTestData.Agent("Jamais vu", lastSeenAtUtc: null, createdAt: Now.AddHours(-30)));
        var rule = new AgentMuteAlertRule(agents, new FakeTenantSettingsQueries());

        var evaluation = await rule.EvaluateAsync(Context);

        evaluation.IsFiring.Should().BeTrue();
        evaluation.Detail.Should().Contain("aucun heartbeat").And.Contain("Jamais vu").And.Contain("enregistré le");
    }

    [Fact]
    public async Task Freshly_Registered_Agent_Without_Heartbeat_Does_Not_Fire()
    {
        var agents = new FakeAgentQueries(
            RuleAlertTestData.Agent("Tout neuf", lastSeenAtUtc: null, createdAt: Now.AddHours(-3)));
        var rule = new AgentMuteAlertRule(agents, new FakeTenantSettingsQueries());

        (await rule.EvaluateAsync(Context)).IsFiring.Should().BeFalse();
    }

    [Fact]
    public async Task Revoked_Agent_Is_Excluded()
    {
        var agents = new FakeAgentQueries(
            RuleAlertTestData.Agent("Révoqué", lastSeenAtUtc: Now.AddDays(-30), createdAt: Now.AddDays(-60), isRevoked: true));
        var rule = new AgentMuteAlertRule(agents, new FakeTenantSettingsQueries());

        (await rule.EvaluateAsync(Context)).IsFiring.Should().BeFalse();
    }

    [Fact]
    public async Task No_Agents_Clears()
    {
        var rule = new AgentMuteAlertRule(new FakeAgentQueries(), new FakeTenantSettingsQueries());

        (await rule.EvaluateAsync(Context)).IsFiring.Should().BeFalse();
    }

    [Fact]
    public async Task Tenant_Override_Tightens_Threshold()
    {
        // Seuil tenant à 1 h : un agent muet depuis 2 h déclenche, là où le défaut (24 h) ne déclencherait pas.
        var agents = new FakeAgentQueries(
            RuleAlertTestData.Agent("Poste", lastSeenAtUtc: Now.AddHours(-2), createdAt: Now.AddDays(-10)));
        var tenantSettings = new FakeTenantSettingsQueries(
            companyId: Guid.NewGuid(),
            thresholds: RuleAlertTestData.Thresholds(agentSilentHours: 1));
        var rule = new AgentMuteAlertRule(agents, tenantSettings);

        (await rule.EvaluateAsync(Context)).IsFiring.Should().BeTrue();
    }

    [Fact]
    public async Task Tenant_Override_Relaxes_Threshold()
    {
        // Seuil tenant à 48 h : un agent muet depuis 30 h ne déclenche pas, là où le défaut (24 h) déclencherait.
        var agents = new FakeAgentQueries(
            RuleAlertTestData.Agent("Poste", lastSeenAtUtc: Now.AddHours(-30), createdAt: Now.AddDays(-10)));
        var tenantSettings = new FakeTenantSettingsQueries(
            companyId: Guid.NewGuid(),
            thresholds: RuleAlertTestData.Thresholds(agentSilentHours: 48));
        var rule = new AgentMuteAlertRule(agents, tenantSettings);

        (await rule.EvaluateAsync(Context)).IsFiring.Should().BeFalse();
    }

    [Fact]
    public async Task Default_Used_When_Company_Present_But_No_Thresholds()
    {
        var agents = new FakeAgentQueries(
            RuleAlertTestData.Agent("Poste", lastSeenAtUtc: Now.AddHours(-25), createdAt: Now.AddDays(-10)));
        var tenantSettings = new FakeTenantSettingsQueries(companyId: Guid.NewGuid(), thresholds: null);
        var rule = new AgentMuteAlertRule(agents, tenantSettings);

        (await rule.EvaluateAsync(Context)).IsFiring.Should().BeTrue();
    }
}
