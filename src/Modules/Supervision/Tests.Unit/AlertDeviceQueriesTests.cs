namespace Liakont.Modules.Supervision.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Domain;
using Liakont.Modules.Supervision.Infrastructure;
using Liakont.Modules.Supervision.Tests.Unit.Doubles;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Lecture du dispositif d'alerte (FIX210, F12 §5) : restitue les 7 règles de F12 §5.2 (3 actives / 4 gelées
/// dérivées des règles enregistrées), leur gravité française, le seuil EFFECTIF du tenant (repli défauts
/// F12 §5.2) et l'état de l'e-mail opérateur (sans exposer l'adresse).
/// </summary>
public sealed class AlertDeviceQueriesTests
{
    private static readonly string[] RegisteredRuleKeys =
        ["agent.mute", "documents.blocked", "documents.pa_rejected"];

    [Fact]
    public async Task Returns_The_Seven_F12_Rules_Three_Active_Four_Frozen()
    {
        var device = await Build().GetDeviceStatusAsync();

        device.Rules.Should().HaveCount(7);
        device.Rules.Count(r => r.IsActive).Should().Be(3);
        device.Rules.Count(r => !r.IsActive).Should().Be(4);
    }

    [Fact]
    public async Task Active_State_Is_Derived_From_Registered_Rules()
    {
        var device = await Build().GetDeviceStatusAsync();

        Status(device, "agent.mute").IsActive.Should().BeTrue();
        Status(device, "documents.blocked").IsActive.Should().BeTrue();
        Status(device, "documents.pa_rejected").IsActive.Should().BeTrue();

        // Règles déclarées mais sans producteur (SUP01c) → gelées.
        Status(device, "agent.missed_run").IsActive.Should().BeFalse();
        Status(device, "push.queue_backlog").IsActive.Should().BeFalse();
        Status(device, "period.deadline_near").IsActive.Should().BeFalse();
        Status(device, "agent.version_obsolete").IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Severity_Labels_Are_French()
    {
        var device = await Build().GetDeviceStatusAsync();

        Status(device, "agent.mute").Severity.Should().Be("Critique");
        Status(device, "push.queue_backlog").Severity.Should().Be("Avertissement");
    }

    [Fact]
    public async Task Threshold_Display_Uses_The_Tenant_Effective_Value()
    {
        var thresholds = RuleAlertTestData.Thresholds(agentSilentHours: 48, blockedDocumentsDays: 9, paRejectionsDays: 1);
        var device = await Build(thresholds: thresholds).GetDeviceStatusAsync();

        Status(device, "agent.mute").ThresholdDisplay.Should().Be("> 48 h");
        Status(device, "documents.blocked").ThresholdDisplay.Should().Be("> 9 j");
        Status(device, "documents.pa_rejected").ThresholdDisplay.Should().Be("> 1 j");
    }

    [Fact]
    public async Task Threshold_Display_Falls_Back_To_F12_Defaults_When_No_Thresholds()
    {
        var device = await Build(thresholds: null).GetDeviceStatusAsync();

        Status(device, "agent.mute").ThresholdDisplay.Should().Be("> 24 h");
        Status(device, "documents.blocked").ThresholdDisplay.Should().Be("> 5 j");
        Status(device, "documents.pa_rejected").ThresholdDisplay.Should().Be("> 2 j");
        Status(device, "agent.missed_run").ThresholdDisplay.Should().Be("> 36 h");
        Status(device, "push.queue_backlog").ThresholdDisplay.Should().Be("> 50 éléments ou > 6 h");
    }

    [Fact]
    public async Task Deadline_Rule_Shows_J_Minus_3_And_Version_Rule_Has_No_Threshold()
    {
        var device = await Build().GetDeviceStatusAsync();

        Status(device, "period.deadline_near").ThresholdDisplay.Should().Be("J-3");
        Status(device, "agent.version_obsolete").ThresholdDisplay.Should().Be("—");
    }

    [Fact]
    public async Task Operator_Email_Configured_Reflects_The_Instance_Option()
    {
        (await Build(operatorEmail: "ops@exemple.test").GetDeviceStatusAsync())
            .OperatorEmailConfigured.Should().BeTrue();

        (await Build(operatorEmail: "   ").GetDeviceStatusAsync())
            .OperatorEmailConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task Evaluation_Interval_Is_Fifteen_Minutes()
    {
        (await Build().GetDeviceStatusAsync()).EvaluationIntervalMinutes.Should().Be(15);
    }

    private static AlertRuleStatusDtoView Status(Contracts.DTOs.AlertDeviceStatusDto device, string ruleKey) =>
        new(device.Rules.Single(r => r.RuleKey == ruleKey));

    private static AlertDeviceQueries Build(
        AlertThresholdsDto? thresholds = null,
        string operatorEmail = "")
    {
        var tenantSettings = new FakeTenantSettingsQueries(companyId: Guid.NewGuid(), thresholds: thresholds);
        var options = Options.Create(new SupervisionNotificationOptions { OperatorEmail = operatorEmail });
        var rules = RegisteredRuleKeys.Select(k => (IAlertRule)new StubRule(k));
        return new AlertDeviceQueries(rules, tenantSettings, options);
    }

    /// <summary>Accès concis à une ligne de règle par sa clé (lisibilité des assertions).</summary>
    private readonly record struct AlertRuleStatusDtoView(Contracts.DTOs.AlertRuleStatusDto Row)
    {
        public bool IsActive => Row.IsActive;

        public string Severity => Row.Severity;

        public string ThresholdDisplay => Row.ThresholdDisplay;
    }

    /// <summary>Règle d'alerte fictive : seule sa clé compte ici (l'évaluation n'est pas sollicitée).</summary>
    private sealed class StubRule : IAlertRule
    {
        public StubRule(string ruleKey) => RuleKey = ruleKey;

        public string RuleKey { get; }

        public AlertSeverity Severity => AlertSeverity.Critical;

        public Task<AlertEvaluation> EvaluateAsync(AlertEvaluationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(AlertEvaluation.Clear());
    }
}
