namespace Stratum.Common.Infrastructure.BugCapture;

public sealed record ExceptionInfo
{
    public required string Message { get; init; }

    public required string Type { get; init; }

    public string? StackTrace { get; init; }

    public ExceptionInfo? InnerException { get; init; }
}
