namespace Stratum.Common.Infrastructure.Tests.Unit.Actions.Chains;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Actions;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Abstractions.Validation;
using Stratum.Common.Infrastructure.Actions.Chains;
using Xunit;

public sealed class ActionChainExecutorTests
{
    [Fact]
    public async Task ExecuteChainAsync_ChainNotFound_Returns_Failure()
    {
        var executor = CreateExecutor();

        var result = await executor.ExecuteChainAsync("nonexistent", CreateContext());

        result.IsSuccess.Should().BeFalse();
        result.Findings.Should().ContainSingle(f => f.Message.Contains("not found"));
    }

    [Fact]
    public async Task ExecuteChainAsync_EmptyChain_Returns_Success()
    {
        var chain = new TestChain("empty", _ => { });
        var executor = CreateExecutor(chains: [chain]);

        var result = await executor.ExecuteChainAsync("empty", CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteChainAsync_Validation_Step_Runs_And_Succeeds()
    {
        var chain = new TestChain("test", b => b.Validate<SuccessValidator>());
        var executor = CreateExecutor(
            chains: [chain],
            validators: [new SuccessValidator()]);

        var result = await executor.ExecuteChainAsync("test", CreateContext());

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteChainAsync_Validation_Failure_Stops_Chain()
    {
        var executionOrder = new List<string>();
        var chain = new TestChain("test", b =>
        {
            b.Validate<FailingValidator>();
            b.Execute<TrackingStep>();
        });

        var executor = CreateExecutor(
            chains: [chain],
            validators: [new FailingValidator()],
            steps: [new TrackingStep(executionOrder)]);

        var result = await executor.ExecuteChainAsync("test", CreateContext());

        result.IsSuccess.Should().BeFalse();
        result.Findings.Should().Contain(f => f.Severity == ActionFindingSeverity.Error);
        executionOrder.Should().BeEmpty("execution step should not run after validation failure");
    }

    [Fact]
    public async Task ExecuteChainAsync_Validate_Then_Execute_In_Order()
    {
        var executionOrder = new List<string>();
        var chain = new TestChain("test", b =>
        {
            b.Validate<SuccessValidator>();
            b.Execute<TrackingStep>();
        });

        var executor = CreateExecutor(
            chains: [chain],
            validators: [new SuccessValidator()],
            steps: [new TrackingStep(executionOrder)]);

        var result = await executor.ExecuteChainAsync("test", CreateContext());

        result.IsSuccess.Should().BeTrue();
        executionOrder.Should().ContainSingle().Which.Should().Be("TrackingStep");
    }

    [Fact]
    public async Task ExecuteChainAsync_Conditional_Step_Skipped_When_False()
    {
        var executionOrder = new List<string>();
        var chain = new TestChain("test", b =>
        {
            b.Execute<TrackingStep>(_ => false);
        });

        var executor = CreateExecutor(
            chains: [chain],
            steps: [new TrackingStep(executionOrder)]);

        var result = await executor.ExecuteChainAsync("test", CreateContext());

        result.IsSuccess.Should().BeTrue();
        executionOrder.Should().BeEmpty("conditional step should be skipped");
    }

    [Fact]
    public async Task ExecuteChainAsync_Conditional_Step_Executes_When_True()
    {
        var executionOrder = new List<string>();
        var chain = new TestChain("test", b =>
        {
            b.Execute<TrackingStep>(_ => true);
        });

        var executor = CreateExecutor(
            chains: [chain],
            steps: [new TrackingStep(executionOrder)]);

        var result = await executor.ExecuteChainAsync("test", CreateContext());

        result.IsSuccess.Should().BeTrue();
        executionOrder.Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteChainAsync_Notification_Failure_Does_Not_Block()
    {
        var chain = new TestChain("test", b =>
        {
            b.Notify<FailingActionStep>();
        });

        var executor = CreateExecutor(
            chains: [chain],
            steps: [new FailingActionStep()]);

        var result = await executor.ExecuteChainAsync("test", CreateContext());

        result.IsSuccess.Should().BeTrue("notification failures should not block the chain");
    }

    [Fact]
    public async Task ExecuteChainAsync_Execution_Step_Failure_Blocks()
    {
        var chain = new TestChain("test", b =>
        {
            b.Execute<FailingActionStep>();
        });

        var executor = CreateExecutor(
            chains: [chain],
            steps: [new FailingActionStep()]);

        var result = await executor.ExecuteChainAsync("test", CreateContext());

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteChainAsync_Unresolvable_Validator_Returns_Failure()
    {
        // SuccessValidator is NOT registered in DI
        var chain = new TestChain("test", b => b.Validate<SuccessValidator>());
        var executor = CreateExecutor(chains: [chain]);

        var result = await executor.ExecuteChainAsync("test", CreateContext());

        result.IsSuccess.Should().BeFalse();
        result.Findings.Should().ContainSingle(f => f.Message.Contains("Could not resolve"));
    }

    [Fact]
    public async Task ExecuteChainAsync_Unresolvable_Step_Returns_Failure()
    {
        // TrackingStep is NOT registered in DI
        var chain = new TestChain("test", b => b.Execute<TrackingStep>());
        var executor = CreateExecutor(chains: [chain]);

        var result = await executor.ExecuteChainAsync("test", CreateContext());

        result.IsSuccess.Should().BeFalse();
        result.Findings.Should().ContainSingle(f => f.Message.Contains("Could not resolve"));
    }

    [Fact]
    public async Task ExecuteChainAsync_Respects_Cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var chain = new TestChain("test", b => b.Execute<TrackingStep>());
        var executor = CreateExecutor(
            chains: [chain],
            steps: [new TrackingStep([])]);

        var context = new ActionContext<TestEntity>
        {
            Entity = new TestEntity(),
            Actor = new FakeActorContext(),
            CancellationToken = cts.Token,
        };

        var act = () => executor.ExecuteChainAsync("test", context);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ActionContext<TestEntity> CreateContext() =>
        new()
        {
            Entity = new TestEntity(),
            Actor = new FakeActorContext(),
        };

    private static ActionChainExecutor CreateExecutor(
        IActionChain<TestEntity>[]? chains = null,
        IEntityValidator<TestEntity>[]? validators = null,
        IActionStep<TestEntity>[]? steps = null)
    {
        var services = new ServiceCollection();

        foreach (var chain in chains ?? [])
        {
            services.AddSingleton<IActionChain<TestEntity>>(chain);
        }

        foreach (var validator in validators ?? [])
        {
            services.AddSingleton(validator.GetType(), validator);
        }

        foreach (var step in steps ?? [])
        {
            services.AddSingleton(step.GetType(), step);
        }

        var sp = services.BuildServiceProvider();
        return new ActionChainExecutor(sp, NullLogger<ActionChainExecutor>.Instance);
    }

    public sealed record TestEntity;

    private sealed class FakeActorContext : IActorContext
    {
        public Guid UserId => Guid.NewGuid();

        public Guid CorrelationId => Guid.NewGuid();

        public bool IsAuthenticated => true;

        public string? DisplayName => "Test";

        public string? Email => "test@test.com";

        public Guid? CompanyId => null;

        public string? Timezone => null;

        public string? Language => null;

        public string? TenantId => null;
    }

    private sealed class TestChain(string name, Action<IActionChainBuilder<TestEntity>> configure)
        : IActionChain<TestEntity>
    {
        public string Name => name;

        public void Configure(IActionChainBuilder<TestEntity> builder) => configure(builder);
    }

    private sealed class SuccessValidator : IEntityValidator<TestEntity>
    {
        public Task<ValidationResult> ValidateAsync(TestEntity entity, IActorContext actor, CancellationToken ct = default)
            => Task.FromResult(ValidationResult.Valid());
    }

    private sealed class FailingValidator : IEntityValidator<TestEntity>
    {
        public Task<ValidationResult> ValidateAsync(TestEntity entity, IActorContext actor, CancellationToken ct = default)
            => Task.FromResult(ValidationResult.Invalid("Validation failed", "field", "ERR-001"));
    }

    private sealed class TrackingStep(List<string> executionOrder) : IActionStep<TestEntity>
    {
        public ActionStage Stage => ActionStage.MainOperation;

        public int Order => 0;

        public Task<ActionResult> ExecuteAsync(ActionContext<TestEntity> context)
        {
            executionOrder.Add("TrackingStep");
            return Task.FromResult(ActionResult.Success());
        }
    }

    private sealed class FailingActionStep : IActionStep<TestEntity>
    {
        public ActionStage Stage => ActionStage.MainOperation;

        public int Order => 0;

        public Task<ActionResult> ExecuteAsync(ActionContext<TestEntity> context)
            => Task.FromResult(ActionResult.Failure("step", "Step failed"));
    }
}
