namespace Stratum.Modules.Notification.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Contracts;
using Stratum.Modules.Notification.Contracts.Commands;

public sealed class UpdateServiceDefinitionHandler : IRequestHandler<UpdateServiceDefinitionCommand>
{
    private readonly INotificationUnitOfWorkFactory _uowFactory;
    private readonly IPermissionService _permissions;

    public UpdateServiceDefinitionHandler(INotificationUnitOfWorkFactory uowFactory, IPermissionService permissions)
    {
        _uowFactory = uowFactory;
        _permissions = permissions;
    }

    public async Task Handle(UpdateServiceDefinitionCommand request, CancellationToken cancellationToken)
    {
        if (!_permissions.HasPermission(NotificationPermissions.ServiceUpdate))
        {
            throw new UnauthorizedAccessException("Missing permission: " + NotificationPermissions.ServiceUpdate);
        }

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        var service = await uow.GetServiceDefinitionByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("ServiceDefinition", request.Id);

        service.Update(
            request.Name,
            request.Email,
            request.Description,
            request.IsActive,
            request.ManagerName,
            request.DefaultSlaHours,
            request.Color,
            request.Competences);

        await uow.UpdateServiceDefinitionAsync(service, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}
