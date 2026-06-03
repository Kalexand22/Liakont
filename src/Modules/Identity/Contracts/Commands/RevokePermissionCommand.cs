namespace Stratum.Modules.Identity.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record RevokePermissionCommand : ICommand
{
    public required string RoleName { get; init; }

    public required string Permission { get; init; }
}
