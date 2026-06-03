namespace Stratum.Modules.Party.Contracts.DTOs;

public record ImportPartiesResultDto
{
    public required int TotalRows { get; init; }

    public required int SuccessCount { get; init; }

    public required int ErrorCount { get; init; }

    public required IReadOnlyList<ImportRowError> Errors { get; init; }

    public required IReadOnlyList<Guid> CreatedPartyIds { get; init; }
}
