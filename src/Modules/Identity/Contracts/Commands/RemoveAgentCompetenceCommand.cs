namespace Stratum.Modules.Identity.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record RemoveAgentCompetenceCommand : ICommand
{
    public required Guid CompetenceId { get; init; }
}
