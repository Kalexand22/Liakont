namespace Stratum.Modules.Party.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record DeactivatePartyCommand : ICommand
{
    public required Guid PartyId { get; init; }
}
