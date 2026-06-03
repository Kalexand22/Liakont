namespace Stratum.Modules.Notification.Contracts.DTOs;

public record IntegrationConfigDto
{
    public required Guid Id { get; init; }

    public required string IntegrationType { get; init; }

    public required string ConfigJson { get; init; }

    public required bool IsEnabled { get; init; }

    public required Guid CompanyId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
