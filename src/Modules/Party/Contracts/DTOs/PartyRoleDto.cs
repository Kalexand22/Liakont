namespace Stratum.Modules.Party.Contracts.DTOs;

public record PartyRoleDto
{
    public required Guid Id { get; init; }

    public required string RoleCode { get; init; }

    public required bool IsActive { get; init; }

    public required DateTimeOffset ActivatedAt { get; init; }

    public DateTimeOffset? DeactivatedAt { get; init; }
}
