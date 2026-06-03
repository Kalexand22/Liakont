namespace Stratum.Modules.Identity.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record AddTeamMemberCommand : ICommand<Guid>
{
    public required Guid TeamId { get; init; }

    public required Guid UserId { get; init; }

    public string? Role { get; init; }
}
