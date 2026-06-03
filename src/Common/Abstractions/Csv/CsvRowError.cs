namespace Stratum.Common.Abstractions.Csv;

public record CsvRowError(int LineNumber, string Field, string Message);
