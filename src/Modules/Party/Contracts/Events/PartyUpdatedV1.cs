namespace Stratum.Modules.Party.Contracts.Events;

public record PartyUpdatedV1
{
    public required Guid PartyId { get; init; }

    public required string LegalName { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
