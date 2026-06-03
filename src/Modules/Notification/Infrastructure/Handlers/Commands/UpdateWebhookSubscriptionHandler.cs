namespace Stratum.Modules.Notification.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Contracts.Commands;

public sealed class UpdateWebhookSubscriptionHandler : IRequestHandler<UpdateWebhookSubscriptionCommand>
{
    private readonly INotificationUnitOfWorkFactory _uowFactory;

    public UpdateWebhookSubscriptionHandler(INotificationUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;
    }

    public async Task Handle(UpdateWebhookSubscriptionCommand request, CancellationToken cancellationToken)
    {
        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        var subscription = await uow.GetWebhookSubscriptionByIdAsync(request.SubscriptionId, cancellationToken)
            ?? throw new NotFoundException("WebhookSubscription", request.SubscriptionId);

        var secret = request.Secret ?? subscription.Secret;
        subscription.Update(request.Name, request.EventType, request.TargetUrl, secret, request.IsActive);

        await uow.UpdateWebhookSubscriptionAsync(subscription, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}
