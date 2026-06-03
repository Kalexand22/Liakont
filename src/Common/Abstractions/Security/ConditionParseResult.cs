namespace Stratum.Common.Abstractions.Security;

public record ConditionParseResult
{
    public required bool IsValid { get; init; }

    public string? ErrorMessage { get; init; }
}
