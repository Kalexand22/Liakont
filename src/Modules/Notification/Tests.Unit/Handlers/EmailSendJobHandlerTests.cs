namespace Stratum.Modules.Notification.Tests.Unit.Handlers;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Modules.Notification.Contracts;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Infrastructure.Handlers.Jobs;
using Stratum.Modules.Notification.Tests.Unit.Fakes;
using Xunit;

public class EmailSendJobHandlerTests
{
    [Fact]
    public async Task HandleAsync_Should_Call_Transport_And_PublishEvent()
    {
        var transport = new FakeEmailTransport();
        var uowFactory = new FakeNotificationUnitOfWorkFactory();
        var handler = new EmailSendJobHandler(transport, uowFactory, NullLogger<EmailSendJobHandler>.Instance);

        var payload = new EmailSendJobPayload
        {
            RecipientEmail = "alice@example.com",
            Subject = "Hello Alice",
            Body = "<p>Welcome</p>",
            TemplateCode = "WELCOME",
            LanguageCode = "en",
        };

        await handler.HandleAsync(payload);

        transport.SentTo.Should().Be("alice@example.com");
        transport.SentSubject.Should().Be("Hello Alice");
        uowFactory.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_Should_Throw_And_PublishFailedEvent_On_TransportFailure()
    {
        var transport = new FailingEmailTransport();
        var uowFactory = new FakeNotificationUnitOfWorkFactory();
        var handler = new EmailSendJobHandler(transport, uowFactory, NullLogger<EmailSendJobHandler>.Instance);

        var payload = new EmailSendJobPayload
        {
            RecipientEmail = "alice@example.com",
            Subject = "Hello",
            Body = "Body",
            TemplateCode = "WELCOME",
            LanguageCode = "en",
        };

        var act = () => handler.HandleAsync(payload);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("SMTP error");
        uowFactory.Committed.Should().BeTrue(); // failed event was published
    }

    private sealed class FakeEmailTransport : IEmailTransport
    {
        public string? SentTo { get; private set; }

        public string? SentSubject { get; private set; }

        public Task SendAsync(string recipient, string subject, string body, CancellationToken ct = default)
        {
            SentTo = recipient;
            SentSubject = subject;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingEmailTransport : IEmailTransport
    {
        public Task SendAsync(string recipient, string subject, string body, CancellationToken ct = default)
            => throw new InvalidOperationException("SMTP error");
    }
}
