namespace Stratum.Modules.Party.Contracts.Events;

public record PartyRoleAssignedV1
{
    public required Guid PartyId { get; init; }

    public required string RoleCode { get; init; }

    public required DateTimeOffset AssignedAt { get; init; }
}
