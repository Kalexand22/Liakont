namespace Stratum.Modules.Notification.Contracts.DTOs;

public record ServiceDefinitionDto
{
    public required Guid Id { get; init; }

    public required string Code { get; init; }

    public required string Name { get; init; }

    public required string Email { get; init; }

    public string? Description { get; init; }

    public required bool IsActive { get; init; }

    public Guid? CompanyId { get; init; }

    public string? ManagerName { get; init; }

    public int? DefaultSlaHours { get; init; }

    public string? Color { get; init; }

    public string? Competences { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
