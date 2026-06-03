namespace Stratum.Modules.Notification.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Contracts.Commands;
using Stratum.Modules.Notification.Domain.Entities;

public sealed class CreateServiceDefinitionHandler : IRequestHandler<CreateServiceDefinitionCommand, Guid>
{
    private readonly INotificationUnitOfWorkFactory _uowFactory;

    public CreateServiceDefinitionHandler(INotificationUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;
    }

    public async Task<Guid> Handle(CreateServiceDefinitionCommand request, CancellationToken cancellationToken)
    {
        var service = ServiceDefinition.Create(
            request.Code,
            request.Name,
            request.Email,
            request.Description,
            request.CompanyId,
            request.ManagerName,
            request.DefaultSlaHours,
            request.Color,
            request.Competences);

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        await uow.InsertServiceDefinitionAsync(service, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        return service.Id;
    }
}
