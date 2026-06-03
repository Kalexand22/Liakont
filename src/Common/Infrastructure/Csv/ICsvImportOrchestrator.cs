namespace Stratum.Common.Infrastructure.Csv;

using Stratum.Common.Abstractions.Csv;

public interface ICsvImportOrchestrator
{
    Task<CsvImportResult> ImportAsync(string importType, Stream csvStream, CancellationToken ct = default);

    bool HasImporter(string importType);
}
