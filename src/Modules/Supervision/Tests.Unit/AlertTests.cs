namespace Liakont.Modules.Supervision.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.Supervision.Domain;
using Xunit;

public sealed class AlertTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Raise_Creates_An_Active_Unacknowledged_Alert()
    {
        var alert = Alert.Raise("acme", "agent.mute", AlertSeverity.Critical, "L'agent ne répond plus.", Now);

        alert.Id.Should().NotBe(Guid.Empty);
        alert.TenantId.Should().Be("acme");
        alert.RuleKey.Should().Be("agent.mute");
        alert.Severity.Should().Be(AlertSeverity.Critical);
        alert.Detail.Should().Be("L'agent ne répond plus.");
        alert.TriggeredUtc.Should().Be(Now);
        alert.ResolvedUtc.Should().BeNull();
        alert.IsActive.Should().BeTrue();
        alert.AcknowledgedBy.Should().BeNull();
        alert.IsAcknowledged.Should().BeFalse();
    }

    [Fact]
    public void Raise_Blanks_Detail_To_Null()
    {
        var alert = Alert.Raise("acme", "agent.mute", AlertSeverity.Warning, "   ", Now);

        alert.Detail.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Raise_Requires_Tenant_And_Rule(string blank)
    {
        var actTenant = () => Alert.Raise(blank, "agent.mute", AlertSeverity.Warning, null, Now);
        var actRule = () => Alert.Raise("acme", blank, AlertSeverity.Warning, null, Now);

        actTenant.Should().Throw<ArgumentException>();
        actRule.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Resolve_Sets_ResolvedUtc_And_Makes_Inactive()
    {
        var alert = Alert.Raise("acme", "agent.mute", AlertSeverity.Critical, null, Now);
        var resolvedAt = Now.AddHours(1);

        alert.Resolve(resolvedAt);

        alert.ResolvedUtc.Should().Be(resolvedAt);
        alert.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Resolve_Twice_Throws()
    {
        var alert = Alert.Raise("acme", "agent.mute", AlertSeverity.Critical, null, Now);
        alert.Resolve(Now.AddHours(1));

        var act = () => alert.Resolve(Now.AddHours(2));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Acknowledge_Records_Operator_Without_Resolving()
    {
        var alert = Alert.Raise("acme", "agent.mute", AlertSeverity.Critical, null, Now);
        var ackAt = Now.AddMinutes(5);

        alert.Acknowledge("operator@instance", ackAt);

        alert.AcknowledgedBy.Should().Be("operator@instance");
        alert.AcknowledgedUtc.Should().Be(ackAt);
        alert.IsAcknowledged.Should().BeTrue();

        // Acquitter ne résout pas : l'alerte reste active tant que la condition persiste.
        alert.IsActive.Should().BeTrue();
        alert.ResolvedUtc.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Acknowledge_Requires_Operator_Identity(string blank)
    {
        var alert = Alert.Raise("acme", "agent.mute", AlertSeverity.Critical, null, Now);

        var act = () => alert.Acknowledge(blank, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Acknowledged_Alert_Can_Still_Resolve()
    {
        var alert = Alert.Raise("acme", "agent.mute", AlertSeverity.Critical, null, Now);
        alert.Acknowledge("operator@instance", Now.AddMinutes(5));

        alert.Resolve(Now.AddHours(1));

        alert.IsActive.Should().BeFalse();
        alert.IsAcknowledged.Should().BeTrue();
    }
}
