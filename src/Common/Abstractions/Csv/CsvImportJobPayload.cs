namespace Stratum.Common.Abstractions.Csv;

public record CsvImportJobPayload
{
    public required string ImportType { get; init; }

    public required string CsvContentBase64 { get; init; }

    public Guid? CompanyId { get; init; }
}
