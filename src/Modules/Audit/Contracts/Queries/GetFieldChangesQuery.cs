namespace Stratum.Modules.Audit.Contracts.Queries;

using MediatR;
using Stratum.Modules.Audit.Contracts.DTOs;

public record GetFieldChangesQuery : IRequest<IReadOnlyList<FieldChangeDto>>
{
    public required string EntityType { get; init; }

    public required string EntityId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}
