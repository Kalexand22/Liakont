namespace Stratum.Modules.Notification.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Contracts.Commands;
using Stratum.Modules.Notification.Domain.Entities;

public sealed class CreateEmailTemplateHandler : IRequestHandler<CreateEmailTemplateCommand, Guid>
{
    private readonly INotificationUnitOfWorkFactory _uowFactory;

    public CreateEmailTemplateHandler(INotificationUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;
    }

    public async Task<Guid> Handle(CreateEmailTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = EmailTemplate.Create(
            request.Code,
            request.SubjectTemplate,
            request.BodyTemplate,
            request.LanguageCode,
            request.CompanyId);

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        await uow.InsertEmailTemplateAsync(template, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        return template.Id;
    }
}
