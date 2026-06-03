namespace Stratum.Common.Abstractions.Csv;

public sealed class CsvParseException : Exception
{
    public CsvParseException(int lineNumber, string field, string message)
        : base(message)
    {
        LineNumber = lineNumber;
        Field = field;
    }

    public int LineNumber { get; }

    public string Field { get; }
}
