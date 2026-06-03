namespace Stratum.Modules.Audit.Contracts.DTOs;

public record AuditSearchResultDto
{
    public required Guid Id { get; init; }

    public required string EntityType { get; init; }

    public required string EntityId { get; init; }

    public required string ActivityType { get; init; }

    public required string Description { get; init; }

    public required string ActorId { get; init; }

    public string? Metadata { get; init; }

    public Guid? CompanyId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required int ChangeCount { get; init; }
}
