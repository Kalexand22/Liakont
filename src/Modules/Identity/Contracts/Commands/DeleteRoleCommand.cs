namespace Stratum.Modules.Identity.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record DeleteRoleCommand : ICommand
{
    public required Guid RoleId { get; init; }
}
