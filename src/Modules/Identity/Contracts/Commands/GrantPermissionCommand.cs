namespace Stratum.Modules.Identity.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

public record GrantPermissionCommand : ICommand
{
    public required string RoleName { get; init; }

    public required string Permission { get; init; }

    public required string ModuleSource { get; init; }

    public string? Condition { get; init; }
}
