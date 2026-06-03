namespace Stratum.Modules.Audit.Contracts.DTOs;

public record AuditPolicyDto
{
    public required Guid Id { get; init; }

    public required string EntityType { get; init; }

    public required string ModuleSource { get; init; }

    public required bool IsEnabled { get; init; }

    public required IReadOnlyList<string> TrackedFields { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
