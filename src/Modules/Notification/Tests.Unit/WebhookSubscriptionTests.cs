namespace Stratum.Modules.Notification.Tests.Unit;

using FluentAssertions;
using Stratum.Modules.Notification.Domain.Entities;
using Xunit;

public class WebhookSubscriptionTests
{
    private const string ValidSecret = "abcdefghijklmnopqrstuvwxyz0123456789";

    [Fact]
    public void Create_Should_Succeed_With_Valid_Parameters()
    {
        var sub = WebhookSubscription.Create(
            "Test Webhook",
            "notification.email.sent",
            "https://example.com/hook",
            ValidSecret,
            Guid.NewGuid());

        sub.Id.Should().NotBeEmpty();
        sub.EventType.Should().Be("notification.email.sent");
        sub.TargetUrl.Should().Be("https://example.com/hook");
        sub.Secret.Should().Be(ValidSecret);
        sub.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_Should_Reject_Empty_EventType()
    {
        var act = () => WebhookSubscription.Create(
            "Test Webhook",
            string.Empty,
            "https://example.com/hook",
            ValidSecret,
            Guid.NewGuid());

        act.Should().Throw<ArgumentException>().WithMessage("*INV-WH-003*");
    }

    [Fact]
    public void Create_Should_Reject_Http_TargetUrl()
    {
        var act = () => WebhookSubscription.Create(
            "Test Webhook",
            "test.event",
            "http://example.com/hook",
            ValidSecret,
            Guid.NewGuid());

        act.Should().Throw<ArgumentException>().WithMessage("*INV-WH-001*");
    }

    [Fact]
    public void Create_Should_Reject_NonUrl_TargetUrl()
    {
        var act = () => WebhookSubscription.Create(
            "Test Webhook",
            "test.event",
            "not-a-url",
            ValidSecret,
            Guid.NewGuid());

        act.Should().Throw<ArgumentException>().WithMessage("*INV-WH-001*");
    }

    [Fact]
    public void Create_Should_Reject_Short_Secret()
    {
        var act = () => WebhookSubscription.Create(
            "Test Webhook",
            "test.event",
            "https://example.com/hook",
            "too-short",
            Guid.NewGuid());

        act.Should().Throw<ArgumentException>().WithMessage("*INV-WH-002*");
    }

    [Fact]
    public void Create_Should_Reject_Empty_Secret()
    {
        var act = () => WebhookSubscription.Create(
            "Test Webhook",
            "test.event",
            "https://example.com/hook",
            string.Empty,
            Guid.NewGuid());

        act.Should().Throw<ArgumentException>().WithMessage("*INV-WH-002*");
    }

    [Fact]
    public void Create_Should_Accept_Secret_Exactly_32_Characters()
    {
        var secret = new string('x', 32);

        var sub = WebhookSubscription.Create(
            "Test Webhook",
            "test.event",
            "https://example.com/hook",
            secret,
            Guid.NewGuid());

        sub.Secret.Should().Be(secret);
    }

    [Fact]
    public void Update_Should_Change_Fields()
    {
        var sub = WebhookSubscription.Create(
            "Test Webhook",
            "test.event",
            "https://example.com/hook",
            ValidSecret,
            Guid.NewGuid());

        var newSecret = new string('y', 40);
        sub.Update("Updated Webhook", "new.event", "https://new.example.com/hook", newSecret, false);

        sub.EventType.Should().Be("new.event");
        sub.TargetUrl.Should().Be("https://new.example.com/hook");
        sub.Secret.Should().Be(newSecret);
        sub.IsActive.Should().BeFalse();
        sub.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Update_Should_Reject_Http_TargetUrl()
    {
        var sub = WebhookSubscription.Create(
            "Test Webhook",
            "test.event",
            "https://example.com/hook",
            ValidSecret,
            Guid.NewGuid());

        var act = () => sub.Update("Test Webhook", "test.event", "http://example.com/hook", ValidSecret, true);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-WH-001*");
    }
}
