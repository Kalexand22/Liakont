namespace Stratum.Modules.Party.Contracts.DTOs;

public record ImportRowError
{
    public required int RowNumber { get; init; }

    public required string Field { get; init; }

    public required string Message { get; init; }
}
