namespace Stratum.Modules.Party.Contracts.Events;

public record PartyContactAddedV1
{
    public required Guid PartyId { get; init; }

    public required Guid ContactId { get; init; }

    public required string ContactType { get; init; }

    public required string Value { get; init; }

    public required DateTimeOffset AddedAt { get; init; }
}
