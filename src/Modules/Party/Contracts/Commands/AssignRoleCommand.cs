namespace Stratum.Modules.Party.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record AssignRoleCommand : ICommand<Guid>
{
    public required Guid PartyId { get; init; }

    public required string RoleCode { get; init; }
}
