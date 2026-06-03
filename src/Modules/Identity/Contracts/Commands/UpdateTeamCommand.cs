namespace Stratum.Modules.Identity.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record UpdateTeamCommand : ICommand
{
    public required Guid TeamId { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public string? ServiceCode { get; init; }

    public bool IsActive { get; init; }
}
