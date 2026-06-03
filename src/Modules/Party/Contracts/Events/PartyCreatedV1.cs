namespace Stratum.Modules.Party.Contracts.Events;

public record PartyCreatedV1
{
    public required Guid PartyId { get; init; }

    public required string LegalName { get; init; }

    public required string PartyType { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
