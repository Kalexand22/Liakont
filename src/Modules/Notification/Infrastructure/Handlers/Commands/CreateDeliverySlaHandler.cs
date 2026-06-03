namespace Stratum.Modules.Notification.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Contracts.Commands;
using Stratum.Modules.Notification.Domain.Entities;

public sealed class CreateDeliverySlaHandler : IRequestHandler<CreateDeliverySlaCommand, Guid>
{
    private readonly INotificationUnitOfWorkFactory _uowFactory;

    public CreateDeliverySlaHandler(INotificationUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;
    }

    public async Task<Guid> Handle(CreateDeliverySlaCommand request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<TemplateCategory>(request.Category, ignoreCase: true, out var category))
        {
            throw new ArgumentException($"Invalid category: '{request.Category}'. Valid values: transactional, routing, escalation, reminder.");
        }

        var sla = DeliverySla.Create(
            category,
            request.MaxDelaySeconds,
            request.EscalationAction,
            request.EscalationRecipient,
            request.CompanyId);

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);
        await uow.InsertDeliverySlaAsync(sla, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        return sla.Id;
    }
}
