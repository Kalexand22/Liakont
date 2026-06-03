namespace Stratum.Modules.Notification.Contracts.DTOs;

public record EmailSendJobPayload
{
    public required string RecipientEmail { get; init; }

    public required string Subject { get; init; }

    public required string Body { get; init; }

    public required string TemplateCode { get; init; }

    public required string LanguageCode { get; init; }

    public Guid? CompanyId { get; init; }
}
