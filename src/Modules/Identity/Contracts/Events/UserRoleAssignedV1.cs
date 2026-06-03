namespace Stratum.Modules.Identity.Contracts.Events;

public record UserRoleAssignedV1
{
    public required Guid UserId { get; init; }

    public required string RoleName { get; init; }

    public required DateTimeOffset AssignedAt { get; init; }
}
