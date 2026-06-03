namespace Stratum.Modules.Party.Contracts.Events;

public record PartyAddressAddedV1
{
    public required Guid PartyId { get; init; }

    public required Guid AddressId { get; init; }

    public required string AddressType { get; init; }

    public required string CountryCode { get; init; }

    public required DateTimeOffset AddedAt { get; init; }
}
