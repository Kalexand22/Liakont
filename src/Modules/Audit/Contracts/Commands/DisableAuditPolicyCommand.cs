namespace Stratum.Modules.Audit.Contracts.Commands;

using MediatR;

public record DisableAuditPolicyCommand : IRequest
{
    public required string EntityType { get; init; }
}
