namespace Stratum.Modules.Notification.Tests.Unit.Handlers;

using FluentAssertions;
using Stratum.Modules.Notification.Contracts.Commands;
using Stratum.Modules.Notification.Domain.Entities;
using Stratum.Modules.Notification.Infrastructure.Handlers.Commands;
using Stratum.Modules.Notification.Tests.Unit.Fakes;
using Xunit;

public class DeleteDeliverySlaHandlerTests
{
    [Fact]
    public async Task Handle_Should_Delete_And_Commit()
    {
        var existing = DeliverySla.Create(TemplateCategory.Transactional, 300, "email", "admin@test.com", null);
        var factory = new FakeNotificationUnitOfWorkFactory(existingSla: existing);
        var handler = new DeleteDeliverySlaHandler(factory);

        await handler.Handle(
            new DeleteDeliverySlaCommand { Id = existing.Id },
            CancellationToken.None);

        factory.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_Throw_When_NotFound()
    {
        var factory = new FakeNotificationUnitOfWorkFactory();
        var handler = new DeleteDeliverySlaHandler(factory);

        var act = () => handler.Handle(
            new DeleteDeliverySlaCommand { Id = Guid.NewGuid() },
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*not found*");
    }
}
