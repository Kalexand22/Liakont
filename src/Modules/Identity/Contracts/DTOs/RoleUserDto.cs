namespace Stratum.Modules.Identity.Contracts.DTOs;

public record RoleUserDto
{
    public required Guid UserId { get; init; }

    public required string Username { get; init; }

    public required string DisplayName { get; init; }

    public required string Email { get; init; }

    public required bool IsActive { get; init; }
}
