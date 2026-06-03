namespace Stratum.Common.UI.Tests.Unit;

using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services.Filters;
using Xunit;

public sealed class GridFilterAIServiceTests
{
    private static readonly IReadOnlyList<ColumnDefinition> InvoiceColumns =
    [
        new("Number", "Numéro", "Invoice", "Number", ColumnDataType.Text, true, "Main", 0),
        new("Customer", "Client", "Invoice", "Customer", ColumnDataType.Text, true, "Main", 1),
        new("Amount", "Montant", "Invoice", "Amount", ColumnDataType.Money, true, "Main", 2),
        new("IssuedOn", "Date émission", "Invoice", "IssuedOn", ColumnDataType.Date, true, "Main", 3),
        new("IsPaid", "Payée", "Invoice", "IsPaid", ColumnDataType.Boolean, true, "Main", 4),
        new("Status", "Statut", "Invoice", "Status", ColumnDataType.Enum, true, "Main", 5, AllowedValues: new[] { "Draft", "Sent", "Paid", "Overdue", "Cancelled" }),
    ];

    [Fact]
    public void IsAvailableShouldReflectProvider()
    {
        var available = BuildService(new StubProvider(isAvailable: true, json: "{\"criteria\":[]}"));
        var unavailable = BuildService(new StubProvider(isAvailable: false, json: null));

        available.IsAvailable.Should().BeTrue();
        unavailable.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateAsyncShouldReturnFailedWhenColumnsEmpty()
    {
        var service = BuildService(new StubProvider(isAvailable: true, json: "{}"));

        var result = await service.GenerateAsync(Array.Empty<ColumnDefinition>(), "anything");

        result.Status.Should().Be(AIFilterProposalStatus.Failed);
        result.Criteria.Should().BeEmpty();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public async Task GenerateAsyncShouldReturnFailedWhenInputBlank(string input)
    {
        var service = BuildService(new StubProvider(isAvailable: true, json: "{}"));

        var result = await service.GenerateAsync(InvoiceColumns, input);

        result.Status.Should().Be(AIFilterProposalStatus.Failed);
        result.Criteria.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateAsyncShouldReturnUnavailableWhenProviderDisabled()
    {
        var service = BuildService(new StubProvider(isAvailable: false, json: null));

        var result = await service.GenerateAsync(InvoiceColumns, "factures impayées");

        result.Status.Should().Be(AIFilterProposalStatus.Unavailable);
        result.Criteria.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateAsyncShouldReturnFailedWhenProviderReportsFailure()
    {
        var service = BuildService(new StubProvider(isAvailable: true, json: null, success: false, error: "boom"));

        var result = await service.GenerateAsync(InvoiceColumns, "anything");

        result.Status.Should().Be(AIFilterProposalStatus.Failed);
        result.ErrorMessage.Should().Contain("boom");
    }

    [Fact]
    public async Task GenerateAsyncShouldParseValidMoneyCriterion()
    {
        const string json = """
            { "criteria": [ { "field": "Amount", "operator": "GreaterThan", "value": 5000 } ] }
            """;
        var service = BuildService(new StubProvider(isAvailable: true, json: json));

        var result = await service.GenerateAsync(InvoiceColumns, "factures > 5000");

        result.Status.Should().Be(AIFilterProposalStatus.Success);
        result.Criteria.Should().ContainSingle();
        var criterion = result.Criteria[0];
        criterion.Field.Should().Be("Amount");
        criterion.Operator.Should().Be(FilterOperator.GreaterThan);
        criterion.Value.Should().Be(5000m);
    }

    [Fact]
    public async Task GenerateAsyncShouldParseBooleanValueFromNativeJsonBool()
    {
        const string json = """
            { "criteria": [ { "field": "IsPaid", "operator": "Equals", "value": false } ] }
            """;
        var service = BuildService(new StubProvider(isAvailable: true, json: json));

        var result = await service.GenerateAsync(InvoiceColumns, "non payées");

        result.Status.Should().Be(AIFilterProposalStatus.Success);
        result.Criteria[0].Value.Should().Be(false);
    }

    [Fact]
    public async Task GenerateAsyncShouldMatchEnumValueCaseInsensitively()
    {
        const string json = """
            { "criteria": [ { "field": "Status", "operator": "Equals", "value": "draft" } ] }
            """;
        var service = BuildService(new StubProvider(isAvailable: true, json: json));

        var result = await service.GenerateAsync(InvoiceColumns, "brouillons");

        result.Status.Should().Be(AIFilterProposalStatus.Success);
        result.Criteria[0].Value.Should().Be("Draft");
    }

    [Fact]
    public async Task GenerateAsyncShouldRejectEnumValueNotInAllowedList()
    {
        const string json = """
            { "criteria": [ { "field": "Status", "operator": "Equals", "value": "Archived" } ] }
            """;
        var service = BuildService(new StubProvider(isAvailable: true, json: json));

        var result = await service.GenerateAsync(InvoiceColumns, "archived invoices");

        result.Status.Should().Be(AIFilterProposalStatus.Empty);
        result.Criteria.Should().BeEmpty();
        result.Warnings.Should().Contain(w => w.Contains("Statut", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenerateAsyncShouldRejectOperatorNotAllowedForType()
    {
        const string json = """
            { "criteria": [ { "field": "IsPaid", "operator": "Contains", "value": true } ] }
            """;
        var service = BuildService(new StubProvider(isAvailable: true, json: json));

        var result = await service.GenerateAsync(InvoiceColumns, "contains paid");

        result.Status.Should().Be(AIFilterProposalStatus.Empty);
        result.Warnings.Should().Contain(w => w.Contains("Contains", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateAsyncShouldAcceptKnownFieldCaseInsensitive()
    {
        const string json = """
            { "criteria": [ { "field": "number", "operator": "Equals", "value": "INV-1" } ] }
            """;
        var service = BuildService(new StubProvider(isAvailable: true, json: json));

        var result = await service.GenerateAsync(InvoiceColumns, "filter by number");

        result.Status.Should().Be(AIFilterProposalStatus.Success);
        result.Criteria[0].Field.Should().Be("Number");
    }

    [Fact]
    public async Task GenerateAsyncShouldSuggestNearestWhenFieldUnknown()
    {
        const string json = """
            { "criteria": [ { "field": "Numbr", "operator": "Equals", "value": "INV-1" } ] }
            """;
        var service = BuildService(new StubProvider(isAvailable: true, json: json));

        var result = await service.GenerateAsync(InvoiceColumns, "filter by numbr");

        result.Status.Should().Be(AIFilterProposalStatus.Empty);
        result.Warnings.Should().Contain(w => w.Contains("Numéro", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenerateAsyncShouldParseBetweenWithValueEnd()
    {
        const string json = """
            { "criteria": [ { "field": "Amount", "operator": "Between", "value": 100, "valueEnd": 500 } ] }
            """;
        var service = BuildService(new StubProvider(isAvailable: true, json: json));

        var result = await service.GenerateAsync(InvoiceColumns, "between 100 and 500");

        result.Status.Should().Be(AIFilterProposalStatus.Success);
        result.Criteria[0].Operator.Should().Be(FilterOperator.Between);
        result.Criteria[0].Value.Should().Be(100m);
        result.Criteria[0].ValueEnd.Should().Be(500m);
    }

    [Fact]
    public async Task GenerateAsyncShouldHandleIsNullWithoutValue()
    {
        const string json = """
            { "criteria": [ { "field": "IsPaid", "operator": "IsNull" } ] }
            """;
        var service = BuildService(new StubProvider(isAvailable: true, json: json));

        var result = await service.GenerateAsync(InvoiceColumns, "null check");

        result.Status.Should().Be(AIFilterProposalStatus.Success);
        result.Criteria[0].Operator.Should().Be(FilterOperator.IsNull);
        result.Criteria[0].Value.Should().BeNull();
    }

    [Fact]
    public async Task GenerateAsyncShouldStripMarkdownCodeFences()
    {
        const string json = """
            ```json
            { "criteria": [ { "field": "Amount", "operator": "GreaterThan", "value": 5000 } ] }
            ```
            """;
        var service = BuildService(new StubProvider(isAvailable: true, json: json));

        var result = await service.GenerateAsync(InvoiceColumns, "amount > 5000");

        result.Status.Should().Be(AIFilterProposalStatus.Success);
        result.Criteria.Should().ContainSingle();
    }

    [Fact]
    public async Task GenerateAsyncShouldExtractFromOpenAiEnvelope()
    {
        const string envelope = """
            {
              "id": "chatcmpl-abc",
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "{ \"criteria\": [ { \"field\": \"Amount\", \"operator\": \"GreaterThan\", \"value\": 1000 } ] }"
                  }
                }
              ]
            }
            """;
        var service = BuildService(new StubProvider(isAvailable: true, json: envelope));

        var result = await service.GenerateAsync(InvoiceColumns, "big invoices");

        result.Status.Should().Be(AIFilterProposalStatus.Success);
        result.Criteria.Should().ContainSingle();
        result.Criteria[0].Value.Should().Be(1000m);
    }

    [Fact]
    public async Task GenerateAsyncShouldPropagateLlmWarnings()
    {
        const string json = """
            {
              "criteria": [],
              "warnings": ["Champ délai inconnu. Suggestion : Date échéance."]
            }
            """;
        var service = BuildService(new StubProvider(isAvailable: true, json: json));

        var result = await service.GenerateAsync(InvoiceColumns, "délai dépassé");

        result.Status.Should().Be(AIFilterProposalStatus.Empty);
        result.Warnings.Should().ContainSingle().Which.Should().Contain("délai");
    }

    [Fact]
    public async Task GenerateAsyncShouldReturnFailedOnProviderException()
    {
        var service = BuildService(new ThrowingProvider());

        var result = await service.GenerateAsync(InvoiceColumns, "anything");

        result.Status.Should().Be(AIFilterProposalStatus.Failed);
    }

    [Fact]
    public async Task GenerateAsyncShouldHonorCancellation()
    {
        var service = BuildService(new CancellingProvider());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await service.GenerateAsync(InvoiceColumns, "anything", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GenerateAsyncShouldPropagateCancellationFromInsideProvider()
    {
        // Regression guard: the provider throws OperationCanceledException *mid-call*
        // (e.g. the HTTP linked CTS fired). The service must propagate the exception
        // rather than swallowing it as Failed.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = BuildService(new MidCallCancellingProvider(cts.Token));

        var act = async () => await service.GenerateAsync(InvoiceColumns, "anything", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GenerateAsyncShouldReturnFailedOnUnparseableJson()
    {
        var service = BuildService(new StubProvider(isAvailable: true, json: "not json at all"));

        var result = await service.GenerateAsync(InvoiceColumns, "anything");

        result.Status.Should().Be(AIFilterProposalStatus.Failed);
    }

    [Fact]
    public void BuildPromptShouldListColumnsAndAllowedOperators()
    {
        var prompt = GridFilterAIService.BuildPrompt(InvoiceColumns, "filtre test");

        prompt.Should().Contain("key=\"Status\"");
        prompt.Should().Contain("valeurs=[Draft, Sent, Paid, Overdue, Cancelled]");
        prompt.Should().Contain("## Opérateurs valides par type");
        prompt.Should().Contain("## Demande utilisateur");
        prompt.Should().Contain("filtre test");
    }

    [Fact]
    public void ExtractJsonObjectShouldHandleNullAndWhitespace()
    {
        GridFilterAIService.ExtractJsonObject(string.Empty).Should().BeNull();
        GridFilterAIService.ExtractJsonObject("   ").Should().BeNull();
        GridFilterAIService.ExtractJsonObject("no braces").Should().BeNull();
    }

    [Fact]
    public void ExtractJsonObjectShouldReturnInnerObjectFromFencedContent()
    {
        var payload = "```json\n{ \"a\": 1 }\n```";

        GridFilterAIService.ExtractJsonObject(payload).Should().Be("{ \"a\": 1 }");
    }

    private static GridFilterAIService BuildService(IGridFilterAIProvider provider)
        => new(provider, NullLogger<GridFilterAIService>.Instance);

    private sealed class StubProvider(bool isAvailable, string? json, bool success = true, string? error = null)
        : IGridFilterAIProvider
    {
        public bool IsAvailable { get; } = isAvailable;

        public Task<GridFilterAIProviderResponse> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
            => Task.FromResult(new GridFilterAIProviderResponse(success, json, error));
    }

    private sealed class ThrowingProvider : IGridFilterAIProvider
    {
        public bool IsAvailable => true;

        public Task<GridFilterAIProviderResponse> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("boom");
    }

    private sealed class CancellingProvider : IGridFilterAIProvider
    {
        public bool IsAvailable => true;

        public Task<GridFilterAIProviderResponse> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new GridFilterAIProviderResponse(true, "{}", null));
        }
    }

    private sealed class MidCallCancellingProvider : IGridFilterAIProvider
    {
        private readonly CancellationToken _trigger;

        public MidCallCancellingProvider(CancellationToken trigger)
        {
            _trigger = trigger;
        }

        public bool IsAvailable => true;

        public Task<GridFilterAIProviderResponse> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
            => throw new OperationCanceledException(_trigger);
    }
}
