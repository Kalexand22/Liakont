namespace Stratum.Modules.Identity.Contracts.DTOs;

public record AgentCompetenceDto
{
    public required Guid Id { get; init; }

    public required Guid UserId { get; init; }

    public required string Name { get; init; }

    public string? Category { get; init; }

    public DateOnly? ValidUntil { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
