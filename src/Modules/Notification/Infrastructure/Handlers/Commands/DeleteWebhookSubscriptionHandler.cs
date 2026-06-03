namespace Stratum.Modules.Notification.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Contracts.Commands;

public sealed class DeleteWebhookSubscriptionHandler : IRequestHandler<DeleteWebhookSubscriptionCommand>
{
    private readonly INotificationUnitOfWorkFactory _uowFactory;

    public DeleteWebhookSubscriptionHandler(INotificationUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;
    }

    public async Task Handle(DeleteWebhookSubscriptionCommand request, CancellationToken cancellationToken)
    {
        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        var subscription = await uow.GetWebhookSubscriptionByIdAsync(request.SubscriptionId, cancellationToken)
            ?? throw new NotFoundException("WebhookSubscription", request.SubscriptionId);

        await uow.DeleteWebhookSubscriptionAsync(subscription.Id, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}
