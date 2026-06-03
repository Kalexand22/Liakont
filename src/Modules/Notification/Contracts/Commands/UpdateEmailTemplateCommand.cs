namespace Stratum.Modules.Notification.Contracts.Commands;

using MediatR;

public record UpdateEmailTemplateCommand : IRequest
{
    public required Guid TemplateId { get; init; }

    public required string SubjectTemplate { get; init; }

    public required string BodyTemplate { get; init; }
}
