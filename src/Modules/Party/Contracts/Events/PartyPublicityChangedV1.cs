namespace Stratum.Modules.Party.Contracts.Events;

public record PartyPublicityChangedV1
{
    public Guid PartyId { get; init; }

    public bool IsPublic { get; init; }

    public DateTimeOffset ChangedAt { get; init; }
}
