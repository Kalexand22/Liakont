namespace Stratum.Modules.Identity.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record AssignUserRoleCommand : ICommand
{
    public required Guid UserId { get; init; }

    public required string RoleName { get; init; }
}
