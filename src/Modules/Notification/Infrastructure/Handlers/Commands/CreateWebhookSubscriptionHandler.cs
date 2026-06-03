namespace Stratum.Modules.Notification.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Contracts.Commands;
using Stratum.Modules.Notification.Domain.Entities;

public sealed class CreateWebhookSubscriptionHandler : IRequestHandler<CreateWebhookSubscriptionCommand, Guid>
{
    private readonly INotificationUnitOfWorkFactory _uowFactory;

    public CreateWebhookSubscriptionHandler(INotificationUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;
    }

    public async Task<Guid> Handle(CreateWebhookSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var subscription = WebhookSubscription.Create(
            request.Name,
            request.EventType,
            request.TargetUrl,
            request.Secret,
            request.CompanyId);

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        await uow.InsertWebhookSubscriptionAsync(subscription, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        return subscription.Id;
    }
}
