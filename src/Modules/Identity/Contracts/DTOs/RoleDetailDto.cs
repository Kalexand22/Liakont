namespace Stratum.Modules.Identity.Contracts.DTOs;

public record RoleDetailDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required bool IsSystem { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required IReadOnlyList<string> GrantedPermissions { get; init; }
}
