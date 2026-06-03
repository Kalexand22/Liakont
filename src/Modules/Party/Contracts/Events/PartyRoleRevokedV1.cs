namespace Stratum.Modules.Party.Contracts.Events;

public record PartyRoleRevokedV1
{
    public required Guid PartyId { get; init; }

    public required string RoleCode { get; init; }

    public required DateTimeOffset RevokedAt { get; init; }
}
