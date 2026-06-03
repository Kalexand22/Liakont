namespace Stratum.Modules.Party.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record RemoveContactCommand : ICommand
{
    public required Guid ContactId { get; init; }

    public required Guid PartyId { get; init; }
}
