namespace Liakont.Modules.Supervision.Tests.Unit;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Domain;
using Liakont.Modules.Supervision.Tests.Unit.Doubles;
using Xunit;

public sealed class AlertEvaluationServiceTests
{
    private const string Tenant = "acme";
    private static readonly DateTimeOffset Now = new(2026, 6, 7, 12, 0, 0, TimeSpan.Zero);

    private static AlertEvaluationService BuildEngine(InMemoryAlertStore store, FixedTimeProvider clock, params IAlertRule[] rules) =>
        new(rules, store, clock);

    [Fact]
    public async Task Firing_Rule_With_No_Active_Alert_Raises_One()
    {
        var store = new InMemoryAlertStore();
        var clock = new FixedTimeProvider(Now);
        var rule = new FakeAlertRule("agent.mute", AlertSeverity.Critical, isFiring: true) { Detail = "muet" };
        var engine = BuildEngine(store, clock, rule);

        var result = await engine.EvaluateAsync(Tenant);

        result.HasFailures.Should().BeFalse();
        result.RulesEvaluated.Should().Be(1);
        store.Active.Should().ContainSingle();
        var alert = store.Active[0];
        alert.TenantId.Should().Be(Tenant);
        alert.RuleKey.Should().Be("agent.mute");
        alert.Severity.Should().Be(AlertSeverity.Critical);
        alert.Detail.Should().Be("muet");
        alert.TriggeredUtc.Should().Be(Now);
    }

    [Fact]
    public async Task Firing_Twice_Raises_Once()
    {
        var store = new InMemoryAlertStore();
        var clock = new FixedTimeProvider(Now);
        var rule = new FakeAlertRule("agent.mute", AlertSeverity.Critical, isFiring: true);
        var engine = BuildEngine(store, clock, rule);

        await engine.EvaluateAsync(Tenant);
        clock.Advance(TimeSpan.FromMinutes(15));
        await engine.EvaluateAsync(Tenant);

        // Anti-bruit : une alerte active ne se re-déclenche pas.
        store.All.Should().ContainSingle();
        store.Active.Should().ContainSingle();
    }

    [Fact]
    public async Task Clear_Resolves_Active_Alert()
    {
        var store = new InMemoryAlertStore();
        var clock = new FixedTimeProvider(Now);
        var rule = new FakeAlertRule("agent.mute", AlertSeverity.Critical, isFiring: true);
        var engine = BuildEngine(store, clock, rule);

        await engine.EvaluateAsync(Tenant);
        rule.IsFiring = false;
        clock.Advance(TimeSpan.FromMinutes(15));
        await engine.EvaluateAsync(Tenant);

        store.Active.Should().BeEmpty();
        store.All.Should().ContainSingle();
        store.All[0].ResolvedUtc.Should().Be(Now.AddMinutes(15));
    }

    [Fact]
    public async Task Non_Firing_With_No_Active_Writes_Nothing()
    {
        var store = new InMemoryAlertStore();
        var clock = new FixedTimeProvider(Now);
        var rule = new FakeAlertRule("agent.mute", AlertSeverity.Warning, isFiring: false);
        var engine = BuildEngine(store, clock, rule);

        await engine.EvaluateAsync(Tenant);

        store.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Refires_After_Resolution_Creates_A_New_Alert()
    {
        var store = new InMemoryAlertStore();
        var clock = new FixedTimeProvider(Now);
        var rule = new FakeAlertRule("agent.mute", AlertSeverity.Critical, isFiring: true);
        var engine = BuildEngine(store, clock, rule);

        await engine.EvaluateAsync(Tenant);                 // raise #1
        rule.IsFiring = false;
        clock.Advance(TimeSpan.FromMinutes(15));
        await engine.EvaluateAsync(Tenant);                 // resolve #1
        rule.IsFiring = true;
        clock.Advance(TimeSpan.FromMinutes(15));
        await engine.EvaluateAsync(Tenant);                 // raise #2

        store.All.Should().HaveCount(2);
        store.Active.Should().ContainSingle();
        store.Active[0].TriggeredUtc.Should().Be(Now.AddMinutes(30));
    }

    [Fact]
    public async Task Multiple_Rules_Are_Dispatched_Independently()
    {
        var store = new InMemoryAlertStore();
        var clock = new FixedTimeProvider(Now);
        var firing = new FakeAlertRule("agent.mute", AlertSeverity.Critical, isFiring: true);
        var quiet = new FakeAlertRule("pa.rejects", AlertSeverity.Critical, isFiring: false);
        var engine = BuildEngine(store, clock, firing, quiet);

        var result = await engine.EvaluateAsync(Tenant);

        result.RulesEvaluated.Should().Be(2);
        store.Active.Should().ContainSingle();
        store.Active[0].RuleKey.Should().Be("agent.mute");
    }

    [Fact]
    public async Task Failing_Rule_Is_Isolated_And_Reported()
    {
        var store = new InMemoryAlertStore();
        var clock = new FixedTimeProvider(Now);
        var broken = new FakeAlertRule("agent.mute") { ThrowOnEvaluate = true };
        var healthy = new FakeAlertRule("pa.rejects", AlertSeverity.Critical, isFiring: true);
        var engine = BuildEngine(store, clock, broken, healthy);

        var result = await engine.EvaluateAsync(Tenant);

        // La règle saine s'évalue malgré la panne de l'autre ; l'échec est remonté, pas avalé.
        result.HasFailures.Should().BeTrue();
        result.RulesEvaluated.Should().Be(1);
        result.Failures.Should().ContainSingle(f => f.RuleKey == "agent.mute");
        store.Active.Should().ContainSingle();
        store.Active[0].RuleKey.Should().Be("pa.rejects");
    }

    [Fact]
    public async Task No_Rules_Produces_No_Alert()
    {
        var store = new InMemoryAlertStore();
        var clock = new FixedTimeProvider(Now);
        var engine = BuildEngine(store, clock);

        var result = await engine.EvaluateAsync(Tenant);

        result.RulesEvaluated.Should().Be(0);
        result.HasFailures.Should().BeFalse();
        store.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_Is_Propagated_Not_Swallowed()
    {
        var store = new InMemoryAlertStore();
        var clock = new FixedTimeProvider(Now);
        var rule = new FakeAlertRule("agent.mute", AlertSeverity.Critical, isFiring: true);
        var engine = BuildEngine(store, clock, rule);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await engine.EvaluateAsync(Tenant, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
