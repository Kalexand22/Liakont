namespace Stratum.Common.Abstractions.Csv;

/// <summary>
/// Base class for typed CSV importers. Modules subclass this per entity type.
/// Orchestrates Parse → Validate (all-or-nothing) → Import.
/// </summary>
public abstract class CsvImporterBase<TRow> : ICsvImporter
{
    public abstract string ImportType { get; }

    public async Task<CsvImportResult> ExecuteAsync(Stream csvStream, CancellationToken ct = default)
    {
        IReadOnlyList<TRow> rows;
        try
        {
            rows = Parse(csvStream);
        }
        catch (CsvParseException ex)
        {
            return CsvImportResult.Failed([new CsvRowError(ex.LineNumber, ex.Field, ex.Message)]);
        }

        if (rows.Count == 0)
        {
            return CsvImportResult.Failed([new CsvRowError(0, string.Empty, "CSV file is empty or contains only headers.")]);
        }

        var errors = Validate(rows);
        if (errors.Count > 0)
        {
            return CsvImportResult.Failed(errors);
        }

        var imported = await ImportAsync(rows, ct);
        return CsvImportResult.Succeeded(imported);
    }

    protected abstract IReadOnlyList<TRow> Parse(Stream csvStream);

    protected abstract IReadOnlyList<CsvRowError> Validate(IReadOnlyList<TRow> rows);

    protected abstract Task<int> ImportAsync(IReadOnlyList<TRow> validRows, CancellationToken ct = default);
}
