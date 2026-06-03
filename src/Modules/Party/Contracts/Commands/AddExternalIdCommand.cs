namespace Stratum.Modules.Party.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record AddExternalIdCommand : ICommand<Guid>
{
    public required Guid PartyId { get; init; }

    public required string SystemCode { get; init; }

    public required string ExternalId { get; init; }
}
