namespace Stratum.Modules.Notification.Contracts.DTOs;

public record TemplateLinkDto
{
    public required string Label { get; init; }

    public required string UrlTemplate { get; init; }
}
