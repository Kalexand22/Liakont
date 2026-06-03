namespace Stratum.Modules.Party.Contracts.Events;

public record ExternalIdAddedV1
{
    public required Guid ExternalIdRecordId { get; init; }

    public required Guid PartyId { get; init; }

    public required string SystemCode { get; init; }

    public required string ExternalId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
