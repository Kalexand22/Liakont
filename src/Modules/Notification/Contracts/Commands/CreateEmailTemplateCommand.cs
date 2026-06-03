namespace Stratum.Modules.Notification.Contracts.Commands;

using MediatR;

public record CreateEmailTemplateCommand : IRequest<Guid>
{
    public required string Code { get; init; }

    public required string SubjectTemplate { get; init; }

    public required string BodyTemplate { get; init; }

    public string LanguageCode { get; init; } = "en";

    public Guid? CompanyId { get; init; }
}
