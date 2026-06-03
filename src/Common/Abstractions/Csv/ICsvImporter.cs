namespace Stratum.Common.Abstractions.Csv;

/// <summary>
/// Non-generic interface for DI resolution by import type.
/// Modules implement <see cref="CsvImporterBase{TRow}"/> instead of this directly.
/// </summary>
public interface ICsvImporter
{
    string ImportType { get; }

    Task<CsvImportResult> ExecuteAsync(Stream csvStream, CancellationToken ct = default);
}
