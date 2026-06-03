namespace Stratum.Modules.Audit.Contracts.Commands;

using MediatR;

public record SetAuditPolicyCommand : IRequest
{
    public required string EntityType { get; init; }

    public required string ModuleSource { get; init; }

    public required bool IsEnabled { get; init; }

    public required IReadOnlyList<string> TrackedFields { get; init; }
}
