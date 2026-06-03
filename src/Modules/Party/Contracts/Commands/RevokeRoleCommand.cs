namespace Stratum.Modules.Party.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record RevokeRoleCommand : ICommand
{
    public required Guid PartyId { get; init; }

    public required string RoleCode { get; init; }
}
