namespace Stratum.Modules.Notification.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Contracts.Commands;

public sealed class DeleteDeliverySlaHandler : IRequestHandler<DeleteDeliverySlaCommand>
{
    private readonly INotificationUnitOfWorkFactory _uowFactory;

    public DeleteDeliverySlaHandler(INotificationUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;
    }

    public async Task Handle(DeleteDeliverySlaCommand request, CancellationToken cancellationToken)
    {
        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        var existing = await uow.GetDeliverySlaByIdAsync(request.Id, cancellationToken);
        if (existing is null)
        {
            throw new InvalidOperationException($"DeliverySla with id '{request.Id}' not found.");
        }

        await uow.DeleteDeliverySlaAsync(request.Id, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}
