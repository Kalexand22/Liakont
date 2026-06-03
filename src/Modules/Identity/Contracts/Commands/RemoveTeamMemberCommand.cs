namespace Stratum.Modules.Identity.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record RemoveTeamMemberCommand : ICommand
{
    public required Guid MemberId { get; init; }
}
