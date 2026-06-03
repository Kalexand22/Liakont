namespace Stratum.Modules.Identity.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record UpdateRoleCommand : ICommand
{
    public required Guid RoleId { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }
}
