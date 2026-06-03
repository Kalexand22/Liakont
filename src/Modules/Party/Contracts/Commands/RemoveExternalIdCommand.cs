namespace Stratum.Modules.Party.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record RemoveExternalIdCommand : ICommand
{
    public required Guid ExternalIdRecordId { get; init; }

    public required Guid PartyId { get; init; }
}
