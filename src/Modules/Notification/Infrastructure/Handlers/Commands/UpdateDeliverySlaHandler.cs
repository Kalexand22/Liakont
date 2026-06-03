namespace Stratum.Modules.Notification.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Contracts.Commands;

public sealed class UpdateDeliverySlaHandler : IRequestHandler<UpdateDeliverySlaCommand>
{
    private readonly INotificationUnitOfWorkFactory _uowFactory;

    public UpdateDeliverySlaHandler(INotificationUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;
    }

    public async Task Handle(UpdateDeliverySlaCommand request, CancellationToken cancellationToken)
    {
        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        var sla = await uow.GetDeliverySlaByIdAsync(request.Id, cancellationToken)
            ?? throw new InvalidOperationException($"DeliverySla '{request.Id}' not found.");

        sla.Update(request.MaxDelaySeconds, request.EscalationAction, request.EscalationRecipient);

        await uow.UpdateDeliverySlaAsync(sla, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}
