namespace Stratum.Modules.Notification.Tests.Unit.Domain;

using FluentAssertions;
using Stratum.Modules.Notification.Domain.Entities;
using Stratum.Modules.Notification.Domain.ValueObjects;
using Xunit;

public class RoutingRuleTests
{
    [Fact]
    public void Create_sets_properties_correctly()
    {
        var conditions = new List<RoutingCondition> { RoutingCondition.Leaf("public", "eq", null) };

        var rule = RoutingRule.Create(
            "communication",
            "Communication",
            "reservation",
            "communication",
            RecipientType.ServiceEmail,
            "comm@commune.fr",
            conditions,
            30,
            null);

        rule.Id.Should().NotBeEmpty();
        rule.Code.Should().Be("communication");
        rule.Name.Should().Be("Communication");
        rule.EntityType.Should().Be("reservation");
        rule.ServiceCode.Should().Be("communication");
        rule.RecipientType.Should().Be(RecipientType.ServiceEmail);
        rule.RecipientValue.Should().Be("comm@commune.fr");
        rule.Conditions.Should().HaveCount(1);
        rule.Priority.Should().Be(30);
        rule.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_throws_on_empty_code()
    {
        var act = () => RoutingRule.Create(string.Empty, "Name", "reservation", "svc", RecipientType.ServiceEmail, "a@b.fr", [], 0, null);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-NOTIF-010*");
    }

    [Fact]
    public void Create_throws_on_empty_entityType()
    {
        var act = () => RoutingRule.Create("code", "Name", string.Empty, "svc", RecipientType.ServiceEmail, "a@b.fr", [], 0, null);

        act.Should().Throw<ArgumentException>().WithMessage("*entityType*");
    }

    [Fact]
    public void Create_throws_on_empty_serviceCode()
    {
        var act = () => RoutingRule.Create("code", "Name", "reservation", string.Empty, RecipientType.ServiceEmail, "a@b.fr", [], 0, null);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-NOTIF-012*");
    }

    [Fact]
    public void Update_modifies_fields()
    {
        var rule = RoutingRule.Create("voirie", "Voirie", "reservation", "voirie", RecipientType.ServiceEmail, "v@c.fr", [], 20, null);

        rule.Update("Voirie Updated", "voirie-2", RecipientType.Role, "admin", [], 10, false);

        rule.Name.Should().Be("Voirie Updated");
        rule.ServiceCode.Should().Be("voirie-2");
        rule.RecipientType.Should().Be(RecipientType.Role);
        rule.RecipientValue.Should().Be("admin");
        rule.Priority.Should().Be(10);
        rule.IsActive.Should().BeFalse();
        rule.UpdatedAt.Should().NotBeNull();
    }
}
