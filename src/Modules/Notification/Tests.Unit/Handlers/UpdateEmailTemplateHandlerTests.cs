namespace Stratum.Modules.Notification.Tests.Unit.Handlers;

using FluentAssertions;
using Stratum.Modules.Notification.Contracts.Commands;
using Stratum.Modules.Notification.Domain.Entities;
using Stratum.Modules.Notification.Infrastructure.Handlers.Commands;
using Stratum.Modules.Notification.Tests.Unit.Fakes;
using Xunit;

public class UpdateEmailTemplateHandlerTests
{
    [Fact]
    public async Task Handle_Should_Update_And_Commit()
    {
        var existing = EmailTemplate.Create("WELCOME", "Old Subject", "<p>Old</p>", "en", null);
        var factory = new FakeNotificationUnitOfWorkFactory(existing);
        var handler = new UpdateEmailTemplateHandler(factory);

        await handler.Handle(
            new UpdateEmailTemplateCommand
            {
                TemplateId = existing.Id,
                SubjectTemplate = "New Subject",
                BodyTemplate = "<p>New</p>",
            },
            CancellationToken.None);

        factory.LastUpdated.Should().NotBeNull();
        factory.LastUpdated!.SubjectTemplate.Should().Be("New Subject");
        factory.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_Throw_When_NotFound()
    {
        var factory = new FakeNotificationUnitOfWorkFactory();
        var handler = new UpdateEmailTemplateHandler(factory);

        var act = () => handler.Handle(
            new UpdateEmailTemplateCommand
            {
                TemplateId = Guid.NewGuid(),
                SubjectTemplate = "New Subject",
                BodyTemplate = "<p>New</p>",
            },
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*not found*");
    }
}
