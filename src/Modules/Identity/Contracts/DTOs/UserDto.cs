namespace Stratum.Modules.Identity.Contracts.DTOs;

public record UserDto
{
    public required Guid Id { get; init; }

    public required string Username { get; init; }

    public required string Email { get; init; }

    public required string DisplayName { get; init; }

    public Guid? PartyId { get; init; }

    public string? ExternalId { get; init; }

    public required bool IsActive { get; init; }

    public DateTimeOffset? LastLoginAt { get; init; }

    public required IReadOnlyList<string> Roles { get; init; }
}
