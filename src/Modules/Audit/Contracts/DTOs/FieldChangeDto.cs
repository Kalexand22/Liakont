namespace Stratum.Modules.Audit.Contracts.DTOs;

public record FieldChangeDto
{
    public required Guid Id { get; init; }

    public required Guid EntryId { get; init; }

    public required string EntityType { get; init; }

    public required string EntityId { get; init; }

    public required string FieldName { get; init; }

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }

    public required string ActorId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
}
