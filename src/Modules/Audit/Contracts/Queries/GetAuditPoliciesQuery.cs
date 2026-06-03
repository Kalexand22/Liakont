namespace Stratum.Modules.Audit.Contracts.Queries;

using MediatR;
using Stratum.Modules.Audit.Contracts.DTOs;

public record GetAuditPoliciesQuery : IRequest<IReadOnlyList<AuditPolicyDto>>;
