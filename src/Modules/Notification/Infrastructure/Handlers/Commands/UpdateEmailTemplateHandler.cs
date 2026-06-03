namespace Stratum.Modules.Notification.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Contracts.Commands;

public sealed class UpdateEmailTemplateHandler : IRequestHandler<UpdateEmailTemplateCommand>
{
    private readonly INotificationUnitOfWorkFactory _uowFactory;

    public UpdateEmailTemplateHandler(INotificationUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;
    }

    public async Task Handle(UpdateEmailTemplateCommand request, CancellationToken cancellationToken)
    {
        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        var template = await uow.GetEmailTemplateByIdAsync(request.TemplateId, cancellationToken)
            ?? throw new InvalidOperationException($"Email template '{request.TemplateId}' not found.");

        template.Update(request.SubjectTemplate, request.BodyTemplate);

        await uow.UpdateEmailTemplateAsync(template, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}
