namespace Stratum.Modules.Party.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record UpdateAddressCommand : ICommand
{
    public required Guid AddressId { get; init; }

    public required Guid PartyId { get; init; }

    public required string AddressType { get; init; }

    public required string Line1 { get; init; }

    public string? Line2 { get; init; }

    public required string City { get; init; }

    public required string PostalCode { get; init; }

    public string? Region { get; init; }

    public required string CountryCode { get; init; }

    public bool IsDefault { get; init; }
}
