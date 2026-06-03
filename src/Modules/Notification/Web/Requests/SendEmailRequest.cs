namespace Stratum.Modules.Notification.Web.Requests;

public record SendEmailRequest
{
    public required string TemplateCode { get; init; }

    public string LanguageCode { get; init; } = "en";

    public required string RecipientEmail { get; init; }

    public Dictionary<string, string>? Placeholders { get; init; }

    public Guid? CompanyId { get; init; }
}
