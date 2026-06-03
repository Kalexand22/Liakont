namespace Stratum.Common.Infrastructure.Tests.Unit.Csv;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Csv;
using Stratum.Common.Infrastructure.Csv;
using Xunit;

public class CsvImportOrchestratorTests
{
    [Fact]
    public async Task ImportAsync_Should_Delegate_To_Matching_Importer()
    {
        var importer = new FakeImporter("products", CsvImportResult.Succeeded(5));
        var orchestrator = CreateOrchestrator(importer);

        var result = await orchestrator.ImportAsync("products", Stream.Null);

        result.Success.Should().BeTrue();
        result.RowsImported.Should().Be(5);
    }

    [Fact]
    public async Task ImportAsync_Should_Throw_When_NoImporterRegistered()
    {
        var orchestrator = CreateOrchestrator();

        var act = () => orchestrator.ImportAsync("unknown", Stream.Null);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*No CSV importer registered*unknown*");
    }

    [Fact]
    public void HasImporter_Should_ReturnTrue_When_ImporterExists()
    {
        var orchestrator = CreateOrchestrator(new FakeImporter("orders"));

        orchestrator.HasImporter("orders").Should().BeTrue();
    }

    [Fact]
    public void HasImporter_Should_ReturnFalse_When_NoImporter()
    {
        var orchestrator = CreateOrchestrator();

        orchestrator.HasImporter("missing").Should().BeFalse();
    }

    [Fact]
    public void HasImporter_Should_BeCaseInsensitive()
    {
        var orchestrator = CreateOrchestrator(new FakeImporter("Products"));

        orchestrator.HasImporter("PRODUCTS").Should().BeTrue();
        orchestrator.HasImporter("products").Should().BeTrue();
    }

    [Fact]
    public async Task ImportAsync_Should_ReturnFailedResult_When_ImporterFails()
    {
        var errors = new List<CsvRowError> { new(1, "Qty", "Must be positive") };
        var importer = new FakeImporter("items", CsvImportResult.Failed(errors));
        var orchestrator = CreateOrchestrator(importer);

        var result = await orchestrator.ImportAsync("items", Stream.Null);

        result.Success.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
    }

    private static CsvImportOrchestrator CreateOrchestrator(params ICsvImporter[] importers) =>
        new(importers, NullLogger<CsvImportOrchestrator>.Instance);

    private sealed class FakeImporter : ICsvImporter
    {
        private readonly CsvImportResult _result;

        public FakeImporter(string importType, CsvImportResult? result = null)
        {
            ImportType = importType;
            _result = result ?? CsvImportResult.Succeeded(0);
        }

        public string ImportType { get; }

        public Task<CsvImportResult> ExecuteAsync(Stream csvStream, CancellationToken ct = default) =>
            Task.FromResult(_result);
    }
}
