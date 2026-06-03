namespace Stratum.Modules.Notification.Tests.Unit.Handlers;

using FluentAssertions;
using Stratum.Modules.Notification.Contracts.Commands;
using Stratum.Modules.Notification.Infrastructure.Handlers.Commands;
using Stratum.Modules.Notification.Tests.Unit.Fakes;
using Xunit;

public class CreateEmailTemplateHandlerTests
{
    [Fact]
    public async Task Handle_Should_Insert_And_Commit()
    {
        var factory = new FakeNotificationUnitOfWorkFactory();
        var handler = new CreateEmailTemplateHandler(factory);

        var result = await handler.Handle(
            new CreateEmailTemplateCommand
            {
                Code = "WELCOME",
                SubjectTemplate = "Welcome {{Name}}",
                BodyTemplate = "<p>Hello {{Name}}</p>",
                LanguageCode = "en",
            },
            CancellationToken.None);

        result.Should().NotBeEmpty();
        factory.LastInserted.Should().NotBeNull();
        factory.LastInserted!.Code.Should().Be("WELCOME");
        factory.Committed.Should().BeTrue();
    }
}
