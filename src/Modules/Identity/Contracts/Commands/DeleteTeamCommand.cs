namespace Stratum.Modules.Identity.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record DeleteTeamCommand : ICommand
{
    public required Guid TeamId { get; init; }
}
