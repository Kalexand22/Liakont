namespace Stratum.Modules.Party.Contracts.Events;

public record PartyDeactivatedV1
{
    public required Guid PartyId { get; init; }

    public required DateTimeOffset DeactivatedAt { get; init; }
}
