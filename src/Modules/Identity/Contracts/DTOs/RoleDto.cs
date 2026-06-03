namespace Stratum.Modules.Identity.Contracts.DTOs;

public record RoleDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required bool IsSystem { get; init; }
}
