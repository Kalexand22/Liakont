namespace Stratum.Modules.Notification.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Contracts;
using Stratum.Modules.Notification.Contracts.Commands;

public sealed class DeleteServiceDefinitionHandler : IRequestHandler<DeleteServiceDefinitionCommand>
{
    private readonly INotificationUnitOfWorkFactory _uowFactory;
    private readonly IPermissionService _permissions;

    public DeleteServiceDefinitionHandler(INotificationUnitOfWorkFactory uowFactory, IPermissionService permissions)
    {
        _uowFactory = uowFactory;
        _permissions = permissions;
    }

    public async Task Handle(DeleteServiceDefinitionCommand request, CancellationToken cancellationToken)
    {
        if (!_permissions.HasPermission(NotificationPermissions.ServiceDelete))
        {
            throw new UnauthorizedAccessException("Missing permission: " + NotificationPermissions.ServiceDelete);
        }

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        var service = await uow.GetServiceDefinitionByIdAsync(request.ServiceDefinitionId, cancellationToken)
            ?? throw new NotFoundException("ServiceDefinition", request.ServiceDefinitionId);

        if (await uow.HasRoutingRulesForServiceCodeAsync(service.Code, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Cannot delete service '{service.Code}': it is referenced by one or more routing rules.");
        }

        await uow.DeleteServiceDefinitionAsync(request.ServiceDefinitionId, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}
