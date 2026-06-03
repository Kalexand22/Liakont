namespace Stratum.Common.Infrastructure.Tests.Unit.Services;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.FieldChange;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Abstractions.UiRules;
using Stratum.Common.UI.Services;
using Xunit;

public sealed class FieldChangeMediatorTests : IAsyncDisposable
{
    private readonly FakeFieldChangeEngine _engine = new();
    private readonly FakeActorContextAccessor _actorAccessor = new();
    private readonly FieldChangeMediator<TestDto> _mediator;
    private readonly TestDto _dto = new();
    private int _stateChangedCount;

    public FieldChangeMediatorTests()
    {
        _mediator = new FieldChangeMediator<TestDto>(
            _engine,
            _actorAccessor,
            NullLogger<FieldChangeMediator<TestDto>>.Instance)
        {
            DebounceMs = 50,
        };

        _mediator.Initialize(_dto, () =>
        {
            _stateChangedCount++;
            return Task.CompletedTask;
        });
    }

    public enum TestStatus
    {
        Inactive,
        Active,
    }

    public async ValueTask DisposeAsync()
    {
        await _mediator.DisposeAsync();
    }

    [Fact]
    public async Task FlushAsync_Calls_Engine_And_Applies_FieldsToSet()
    {
        _engine.NextResult = FieldChangeResult.WithFields(
            new Dictionary<string, object?> { ["Name"] = "Updated" });

        _mediator.NotifyFieldChanged("Name");
        await _mediator.FlushAsync();

        _dto.Name.Should().Be("Updated");
        _stateChangedCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task FlushAsync_With_Empty_Queue_Does_Nothing()
    {
        await _mediator.FlushAsync();

        _engine.CallCount.Should().Be(0);
        _stateChangedCount.Should().Be(0);
    }

    [Fact]
    public async Task FlushAsync_Updates_CurrentUiAttributes()
    {
        var attrs = new UiAttributeSet(
            new Dictionary<string, UiFieldAttributes>
            {
                ["Name"] = new UiFieldAttributes { ReadOnly = true },
            });
        _engine.NextResult = FieldChangeResult.WithFieldsAndUi(
            new Dictionary<string, object?>(), attrs);

        _mediator.NotifyFieldChanged("Name");
        await _mediator.FlushAsync();

        _mediator.CurrentUiAttributes.Should().BeSameAs(attrs);
    }

    [Fact]
    public async Task Debounce_Delays_Processing()
    {
        _engine.NextResult = FieldChangeResult.WithFields(
            new Dictionary<string, object?> { ["Count"] = 42 });

        _mediator.NotifyFieldChanged("Count");

        // Not processed yet (debounce pending)
        _engine.CallCount.Should().Be(0);

        // Flush triggers processing deterministically instead of racing the debounce timer
        await _mediator.FlushAsync();

        _engine.CallCount.Should().Be(1);
        _dto.Count.Should().Be(42);
    }

    [Fact]
    public async Task Multiple_Rapid_Notifications_Are_Batched()
    {
        _engine.NextResult = FieldChangeResult.Empty();

        _mediator.NotifyFieldChanged("Name");
        _mediator.NotifyFieldChanged("Count");
        _mediator.NotifyFieldChanged("Name");
        await _mediator.FlushAsync();

        _engine.CallCount.Should().Be(1);
        _engine.LastChangedFields.Should().Contain("Name").And.Contain("Count");
    }

    [Fact]
    public async Task OnChangesProcessed_Callback_Is_Invoked()
    {
        _engine.NextResult = FieldChangeResult.Empty();
        FieldChangeResult? received = null;
        _mediator.OnChangesProcessed = result =>
        {
            received = result;
            return Task.CompletedTask;
        };

        _mediator.NotifyFieldChanged("Name");
        await _mediator.FlushAsync();

        received.Should().NotBeNull();
    }

    [Fact]
    public async Task OnError_Callback_Is_Invoked_On_Engine_Failure()
    {
        _engine.ShouldThrow = true;
        Exception? receivedError = null;
        _mediator.OnError = ex => receivedError = ex;

        _mediator.NotifyFieldChanged("Name");
        await _mediator.FlushAsync();

        receivedError.Should().NotBeNull();
        receivedError.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task ConvertValue_Handles_Enum_From_String()
    {
        _engine.NextResult = FieldChangeResult.WithFields(
            new Dictionary<string, object?> { ["Status"] = "Active" });

        _mediator.NotifyFieldChanged("Status");
        await _mediator.FlushAsync();

        _dto.Status.Should().Be(TestStatus.Active);
    }

    [Fact]
    public async Task ConvertValue_Handles_Same_Type_Without_Conversion()
    {
        _engine.NextResult = FieldChangeResult.WithFields(
            new Dictionary<string, object?> { ["Name"] = "Hello" });

        _mediator.NotifyFieldChanged("Name");
        await _mediator.FlushAsync();

        _dto.Name.Should().Be("Hello");
    }

    [Fact]
    public void NotifyFieldChanged_Without_Initialize_Throws()
    {
        var mediator = new FieldChangeMediator<TestDto>(
            _engine,
            _actorAccessor,
            NullLogger<FieldChangeMediator<TestDto>>.Instance);

        var act = () => mediator.NotifyFieldChanged("Name");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task DisposeAsync_Cancels_Pending_Debounce()
    {
        _engine.NextResult = FieldChangeResult.WithFields(
            new Dictionary<string, object?> { ["Count"] = 99 });

        _mediator.NotifyFieldChanged("Count");
        await _mediator.DisposeAsync();

        // Wait longer than debounce
        await Task.Delay(150);

        _engine.CallCount.Should().Be(0);
    }

    public sealed class TestDto
    {
        public string Name { get; set; } = string.Empty;

        public int Count { get; set; }

        public TestStatus Status { get; set; }
    }

    private sealed class FakeFieldChangeEngine : IFieldChangeEngine
    {
        public FieldChangeResult NextResult { get; set; } = FieldChangeResult.Empty();

        public bool ShouldThrow { get; set; }

        public int CallCount { get; private set; }

        public IReadOnlySet<string>? LastChangedFields { get; private set; }

        public Task<FieldChangeResult> ProcessChangesAsync<T>(
            T entity,
            IReadOnlySet<string> changedFields,
            IActorContext actor,
            CancellationToken ct = default)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Engine failure");
            }

            CallCount++;
            LastChangedFields = changedFields;
            return Task.FromResult(NextResult);
        }
    }

    private sealed class FakeActorContextAccessor : IActorContextAccessor
    {
        public IActorContext Current { get; } = new FakeActorContext();
    }

    private sealed class FakeActorContext : IActorContext
    {
        public Guid UserId => Guid.Empty;

        public Guid CorrelationId => Guid.Empty;

        public bool IsAuthenticated => true;

        public string? DisplayName => "Test User";

        public string? Email => "test@example.com";

        public Guid? CompanyId => null;

        public string? Timezone => null;

        public string? Language => null;

        public string? TenantId => null;
    }
}
