namespace Liakont.Modules.Supervision.Tests.Unit;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Domain;
using Liakont.Modules.Supervision.Tests.Unit.Doubles;
using Xunit;

/// <summary>
/// Vérifie que le moteur (SUP01a) notifie aux SEULES transitions (déclenchement / résolution) — c'est ce
/// qui garantit l'anti-spam de SUP03 (un email au déclenchement, jamais de répétition tant que l'alerte
/// reste active).
/// </summary>
public sealed class AlertEvaluationServiceNotificationTests
{
    private const string Tenant = "acme";
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);

    private static AlertEvaluationService BuildEngine(
        InMemoryAlertStore store,
        FixedTimeProvider clock,
        RecordingAlertNotifier notifier,
        params IAlertRule[] rules) =>
        new(rules, store, clock, notifier);

    [Fact]
    public async Task Notifies_Raised_Once_On_First_Firing()
    {
        var store = new InMemoryAlertStore();
        var clock = new FixedTimeProvider(Now);
        var notifier = new RecordingAlertNotifier();
        var rule = new FakeAlertRule("agent.mute", AlertSeverity.Critical, isFiring: true);
        var engine = BuildEngine(store, clock, notifier, rule);

        await engine.EvaluateAsync(Tenant);

        notifier.Raised.Should().ContainSingle();
        notifier.Raised[0].RuleKey.Should().Be("agent.mute");
        notifier.Resolved.Should().BeEmpty();
    }

    [Fact]
    public async Task Does_Not_Notify_Again_While_Alert_Stays_Active()
    {
        var store = new InMemoryAlertStore();
        var clock = new FixedTimeProvider(Now);
        var notifier = new RecordingAlertNotifier();
        var rule = new FakeAlertRule("agent.mute", AlertSeverity.Critical, isFiring: true);
        var engine = BuildEngine(store, clock, notifier, rule);

        await engine.EvaluateAsync(Tenant);
        clock.Advance(TimeSpan.FromMinutes(15));
        await engine.EvaluateAsync(Tenant);

        // Anti-spam : une alerte active ne re-notifie pas.
        notifier.Raised.Should().ContainSingle();
    }

    [Fact]
    public async Task Notifies_Resolved_On_Auto_Resolution()
    {
        var store = new InMemoryAlertStore();
        var clock = new FixedTimeProvider(Now);
        var notifier = new RecordingAlertNotifier();
        var rule = new FakeAlertRule("agent.mute", AlertSeverity.Critical, isFiring: true);
        var engine = BuildEngine(store, clock, notifier, rule);

        await engine.EvaluateAsync(Tenant);
        rule.IsFiring = false;
        clock.Advance(TimeSpan.FromMinutes(15));
        await engine.EvaluateAsync(Tenant);

        notifier.Raised.Should().ContainSingle();
        notifier.Resolved.Should().ContainSingle();
        notifier.Resolved[0].RuleKey.Should().Be("agent.mute");
    }

    [Fact]
    public async Task Does_Not_Notify_When_Rule_Stays_Clear()
    {
        var store = new InMemoryAlertStore();
        var clock = new FixedTimeProvider(Now);
        var notifier = new RecordingAlertNotifier();
        var rule = new FakeAlertRule("agent.mute", AlertSeverity.Warning, isFiring: false);
        var engine = BuildEngine(store, clock, notifier, rule);

        await engine.EvaluateAsync(Tenant);

        notifier.Raised.Should().BeEmpty();
        notifier.Resolved.Should().BeEmpty();
    }
}
