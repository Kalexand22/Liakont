namespace Stratum.Modules.Party.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record SetPartyPublicCommand : ICommand
{
    public required Guid PartyId { get; init; }

    public required bool IsPublic { get; init; }
}
