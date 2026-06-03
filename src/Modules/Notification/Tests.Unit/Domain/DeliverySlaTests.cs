namespace Stratum.Modules.Notification.Tests.Unit.Domain;

using FluentAssertions;
using Stratum.Modules.Notification.Domain.Entities;
using Xunit;

public class DeliverySlaTests
{
    [Fact]
    public void Create_Should_Initialize_SlaWithValidData()
    {
        var sla = DeliverySla.Create(
            TemplateCategory.Transactional,
            120,
            "log_warning",
            null,
            null);

        sla.Id.Should().NotBeEmpty();
        sla.Category.Should().Be(TemplateCategory.Transactional);
        sla.MaxDelaySeconds.Should().Be(120);
        sla.EscalationAction.Should().Be("log_warning");
        sla.EscalationRecipient.Should().BeNull();
    }

    [Fact]
    public void Create_Should_Throw_When_MaxDelay_Is_Zero()
    {
        var act = () => DeliverySla.Create(TemplateCategory.Routing, 0, null, null, null);
        act.Should().Throw<ArgumentException>().WithMessage("*INV-NOTIF-030*");
    }

    [Fact]
    public void Create_Should_Throw_When_MaxDelay_Is_Negative()
    {
        var act = () => DeliverySla.Create(TemplateCategory.Routing, -1, null, null, null);
        act.Should().Throw<ArgumentException>().WithMessage("*INV-NOTIF-030*");
    }

    [Fact]
    public void Update_Should_ChangeValues()
    {
        var sla = DeliverySla.Create(TemplateCategory.Transactional, 120, null, null, null);

        sla.Update(300, "escalate_to_admin", "admin@test.com");

        sla.MaxDelaySeconds.Should().Be(300);
        sla.EscalationAction.Should().Be("escalate_to_admin");
        sla.EscalationRecipient.Should().Be("admin@test.com");
        sla.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Update_Should_Throw_When_MaxDelay_Is_Zero()
    {
        var sla = DeliverySla.Create(TemplateCategory.Routing, 120, null, null, null);

        var act = () => sla.Update(0, null, null);
        act.Should().Throw<ArgumentException>().WithMessage("*INV-NOTIF-030*");
    }

    [Fact]
    public void Reconstitute_Should_PreserveAllFields()
    {
        var id = Guid.NewGuid();
        var created = DateTimeOffset.UtcNow.AddDays(-1);
        var updated = DateTimeOffset.UtcNow;

        var sla = DeliverySla.Reconstitute(
            id,
            TemplateCategory.Escalation,
            600,
            "escalate",
            "admin@test.com",
            Guid.NewGuid(),
            created,
            updated);

        sla.Id.Should().Be(id);
        sla.Category.Should().Be(TemplateCategory.Escalation);
        sla.MaxDelaySeconds.Should().Be(600);
        sla.CreatedAt.Should().Be(created);
        sla.UpdatedAt.Should().Be(updated);
    }
}
