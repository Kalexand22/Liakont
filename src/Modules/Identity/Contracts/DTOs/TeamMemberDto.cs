namespace Stratum.Modules.Identity.Contracts.DTOs;

public record TeamMemberDto
{
    public required Guid Id { get; init; }

    public required Guid TeamId { get; init; }

    public required Guid UserId { get; init; }

    public required string Username { get; init; }

    public string? DisplayName { get; init; }

    public string? Role { get; init; }

    public DateTimeOffset JoinedAt { get; init; }
}
