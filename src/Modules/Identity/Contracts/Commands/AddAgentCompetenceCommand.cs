namespace Stratum.Modules.Identity.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record AddAgentCompetenceCommand : ICommand<Guid>
{
    public required Guid UserId { get; init; }

    public required string Name { get; init; }

    public string? Category { get; init; }

    public DateOnly? ValidUntil { get; init; }
}
