namespace Stratum.Modules.Notification.Tests.Unit.Handlers;

using FluentAssertions;
using Stratum.Modules.Notification.Contracts.Commands;
using Stratum.Modules.Notification.Domain.Entities;
using Stratum.Modules.Notification.Infrastructure.Handlers.Commands;
using Stratum.Modules.Notification.Tests.Unit.Fakes;
using Xunit;

public class UpdateDeliverySlaHandlerTests
{
    [Fact]
    public async Task Handle_Should_Update_And_Commit()
    {
        var existing = DeliverySla.Create(TemplateCategory.Transactional, 300, "email", "admin@test.com", null);
        var factory = new FakeNotificationUnitOfWorkFactory(existingSla: existing);
        var handler = new UpdateDeliverySlaHandler(factory);

        await handler.Handle(
            new UpdateDeliverySlaCommand
            {
                Id = existing.Id,
                MaxDelaySeconds = 600,
                EscalationAction = "webhook",
                EscalationRecipient = "ops@test.com",
            },
            CancellationToken.None);

        factory.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_Throw_When_NotFound()
    {
        var factory = new FakeNotificationUnitOfWorkFactory();
        var handler = new UpdateDeliverySlaHandler(factory);

        var act = () => handler.Handle(
            new UpdateDeliverySlaCommand
            {
                Id = Guid.NewGuid(),
                MaxDelaySeconds = 600,
            },
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*not found*");
    }
}
