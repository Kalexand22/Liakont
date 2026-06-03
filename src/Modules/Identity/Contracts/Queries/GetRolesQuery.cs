namespace Stratum.Modules.Identity.Contracts.Queries;

using Stratum.Common.Abstractions.Messaging;
using Stratum.Modules.Identity.Contracts.DTOs;

public record GetRolesQuery : IQuery<IReadOnlyList<RoleDto>>;
