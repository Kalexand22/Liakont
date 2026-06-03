namespace Stratum.Common.Infrastructure.BugCapture;

public sealed record MediaCapture
{
    public required Guid Id { get; init; }

    public required string MediaType { get; init; }

    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    public required string MimeType { get; init; }

    public required long FileSizeBytes { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }

    public string? Description { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public required int Sequence { get; init; }
}
