namespace Stratum.Modules.Identity.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record CreateTeamCommand : ICommand<Guid>
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public string? ServiceCode { get; init; }
}
