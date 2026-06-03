namespace Stratum.Modules.Notification.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Contracts.Commands;

public sealed class DeleteRoutingRuleHandler : IRequestHandler<DeleteRoutingRuleCommand>
{
    private readonly INotificationUnitOfWorkFactory _uowFactory;

    public DeleteRoutingRuleHandler(INotificationUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;
    }

    public async Task Handle(DeleteRoutingRuleCommand request, CancellationToken cancellationToken)
    {
        await using var uow = await _uowFactory.BeginAsync(cancellationToken);
        await uow.DeleteRoutingRuleAsync(request.RoutingRuleId, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}
