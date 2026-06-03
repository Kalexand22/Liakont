namespace Stratum.Common.Infrastructure.Csv;

using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Csv;

public sealed partial class CsvImportOrchestrator : ICsvImportOrchestrator
{
    private readonly IEnumerable<ICsvImporter> _importers;
    private readonly ILogger<CsvImportOrchestrator> _logger;

    public CsvImportOrchestrator(
        IEnumerable<ICsvImporter> importers,
        ILogger<CsvImportOrchestrator> logger)
    {
        _importers = importers;
        _logger = logger;
    }

    public bool HasImporter(string importType) =>
        _importers.Any(i => string.Equals(i.ImportType, importType, StringComparison.OrdinalIgnoreCase));

    public async Task<CsvImportResult> ImportAsync(string importType, Stream csvStream, CancellationToken ct = default)
    {
        var importer = _importers.FirstOrDefault(
            i => string.Equals(i.ImportType, importType, StringComparison.OrdinalIgnoreCase));

        if (importer is null)
        {
            throw new ArgumentException($"No CSV importer registered for type '{importType}'.", nameof(importType));
        }

        LogImportStarted(_logger, importType);

        var result = await importer.ExecuteAsync(csvStream, ct);

        if (result.Success)
        {
            LogImportSucceeded(_logger, importType, result.RowsImported);
        }
        else
        {
            LogImportFailed(_logger, importType, result.Errors.Count);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "CSV import started for type '{ImportType}'")]
    private static partial void LogImportStarted(ILogger logger, string importType);

    [LoggerMessage(Level = LogLevel.Information, Message = "CSV import succeeded for type '{ImportType}': {RowCount} rows imported")]
    private static partial void LogImportSucceeded(ILogger logger, string importType, int rowCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "CSV import failed for type '{ImportType}': {ErrorCount} errors")]
    private static partial void LogImportFailed(ILogger logger, string importType, int errorCount);
}
