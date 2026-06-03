namespace Stratum.Common.Abstractions.Csv;

public record CsvImportResult
{
    public required bool Success { get; init; }

    public int RowsImported { get; init; }

    public IReadOnlyList<CsvRowError> Errors { get; init; } = [];

    public Guid? JobId { get; init; }

    public static CsvImportResult Succeeded(int rowsImported) =>
        new() { Success = true, RowsImported = rowsImported };

    public static CsvImportResult Failed(IReadOnlyList<CsvRowError> errors) =>
        new() { Success = false, Errors = errors };

    public static CsvImportResult DeferredToJob(Guid jobId) =>
        new() { Success = true, JobId = jobId };
}
