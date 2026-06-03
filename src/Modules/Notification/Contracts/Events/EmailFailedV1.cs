namespace Stratum.Modules.Notification.Contracts.Events;

public record EmailFailedV1
{
    public required string RecipientEmail { get; init; }

    public required string TemplateCode { get; init; }

    public required string LanguageCode { get; init; }

    public Guid? CompanyId { get; init; }

    public required string ErrorMessage { get; init; }

    public required DateTimeOffset FailedAt { get; init; }
}
