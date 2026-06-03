namespace Stratum.Common.Infrastructure.BugCapture;

public sealed record HttpTrafficRecord
{
    public required DateTimeOffset Timestamp { get; init; }

    public required string Method { get; init; }

    public required string Url { get; init; }

    public required int StatusCode { get; init; }

    public required long DurationMs { get; init; }

    public IReadOnlyDictionary<string, string>? RequestHeaders { get; init; }

    public string? RequestBody { get; init; }

    public IReadOnlyDictionary<string, string>? ResponseHeaders { get; init; }

    public string? ResponseBody { get; init; }

    public bool RequestBodyTruncated { get; init; }

    public bool ResponseBodyTruncated { get; init; }

    public required bool IsError { get; init; }
}
