namespace Stratum.Modules.Party.Contracts.Queries;

using Stratum.Common.Abstractions.Messaging;
using Stratum.Modules.Party.Contracts.DTOs;

public record SearchPartiesQuery : IQuery<IReadOnlyList<PartyDto>>
{
    public string? NameTerm { get; init; }

    public string? RoleCode { get; init; }

    public bool? IsActive { get; init; }

    public int Limit { get; init; } = 50;

    public int Offset { get; init; }
}
