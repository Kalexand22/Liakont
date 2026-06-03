namespace Stratum.Common.Abstractions.Tests.Unit.Csv;

using FluentAssertions;
using Stratum.Common.Abstractions.Csv;
using Xunit;

public class CsvImporterBaseTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReturnSuccess_When_AllRowsValid()
    {
        var importer = new FakeImporter(
            parseResult: [new FakeRow("Alice"), new FakeRow("Bob")],
            validateResult: []);

        var result = await importer.ExecuteAsync(Stream.Null);

        result.Success.Should().BeTrue();
        result.RowsImported.Should().Be(2);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnFailed_When_ValidationErrors()
    {
        var errors = new List<CsvRowError> { new(2, "Name", "Name is required") };
        var importer = new FakeImporter(
            parseResult: [new FakeRow("Alice"), new FakeRow(string.Empty)],
            validateResult: errors);

        var result = await importer.ExecuteAsync(Stream.Null);

        result.Success.Should().BeFalse();
        result.RowsImported.Should().Be(0);
        result.Errors.Should().HaveCount(1);
        result.Errors[0].LineNumber.Should().Be(2);
        result.Errors[0].Field.Should().Be("Name");
    }

    [Fact]
    public async Task ExecuteAsync_Should_NotImport_When_AnyRowInvalid()
    {
        var errors = new List<CsvRowError> { new(3, "Email", "Invalid email") };
        var importer = new FakeImporter(
            parseResult: [new FakeRow("Alice"), new FakeRow("Bob"), new FakeRow("Charlie")],
            validateResult: errors);

        var result = await importer.ExecuteAsync(Stream.Null);

        result.Success.Should().BeFalse();
        importer.ImportCallCount.Should().Be(0, "Import should not be called when validation fails");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnFailed_When_CsvIsEmpty()
    {
        var importer = new FakeImporter(
            parseResult: [],
            validateResult: []);

        var result = await importer.ExecuteAsync(Stream.Null);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Message.Should().Contain("empty");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnFailed_When_ParseThrowsCsvParseException()
    {
        var importer = new FakeImporter(
            parseException: new CsvParseException(5, "Date", "Invalid date format"));

        var result = await importer.ExecuteAsync(Stream.Null);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].LineNumber.Should().Be(5);
        result.Errors[0].Field.Should().Be("Date");
    }

    private sealed record FakeRow(string Name);

    private sealed class FakeImporter : CsvImporterBase<FakeRow>
    {
        private readonly IReadOnlyList<FakeRow> _parseResult;
        private readonly IReadOnlyList<CsvRowError> _validateResult;
        private readonly CsvParseException? _parseException;

        public FakeImporter(
            IReadOnlyList<FakeRow>? parseResult = null,
            IReadOnlyList<CsvRowError>? validateResult = null,
            CsvParseException? parseException = null)
        {
            _parseResult = parseResult ?? [];
            _validateResult = validateResult ?? [];
            _parseException = parseException;
        }

        public override string ImportType => "fake";

        public int ImportCallCount { get; private set; }

        protected override IReadOnlyList<FakeRow> Parse(Stream csvStream)
        {
            if (_parseException is not null)
            {
                throw _parseException;
            }

            return _parseResult;
        }

        protected override IReadOnlyList<CsvRowError> Validate(IReadOnlyList<FakeRow> rows) => _validateResult;

        protected override Task<int> ImportAsync(IReadOnlyList<FakeRow> validRows, CancellationToken ct = default)
        {
            ImportCallCount++;
            return Task.FromResult(validRows.Count);
        }
    }
}
