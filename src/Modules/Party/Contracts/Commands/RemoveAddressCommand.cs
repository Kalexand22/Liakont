namespace Stratum.Modules.Party.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record RemoveAddressCommand : ICommand
{
    public required Guid AddressId { get; init; }

    public required Guid PartyId { get; init; }
}
