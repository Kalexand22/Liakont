namespace Stratum.Modules.Notification.Contracts.DTOs;

public record EmailTemplateDto
{
    public required Guid Id { get; init; }

    public required string Code { get; init; }

    public required string SubjectTemplate { get; init; }

    public required string BodyTemplate { get; init; }

    public required string LanguageCode { get; init; }

    public string Category { get; init; } = "transactional";

    public string? EntityType { get; init; }

    public IReadOnlyList<TemplateLinkDto> TemplateLinks { get; init; } = [];

    public Guid? CompanyId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
