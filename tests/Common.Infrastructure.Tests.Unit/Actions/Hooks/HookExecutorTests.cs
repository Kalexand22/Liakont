namespace Stratum.Common.Infrastructure.Tests.Unit.Actions.Hooks;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Actions;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Infrastructure.Actions.Hooks;
using Xunit;

public sealed class HookExecutorTests : IDisposable
{
    public HookExecutorTests()
    {
        // Each test starts with a clean cache
        HookExecutor.ResetCache();
    }

    public void Dispose()
    {
        HookExecutor.ResetCache();
    }

    [Fact]
    public async Task ExecuteHooksAsync_NoHooks_Returns_Success()
    {
        var executor = CreateExecutor();

        var result = await executor.ExecuteHooksAsync("any.action", ActionStage.PreValidation, CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteHooksAsync_NoMatchingHooks_Returns_Success()
    {
        var hook = new SampleHook();
        var executor = CreateExecutor(hook);

        var result = await executor.ExecuteHooksAsync("nonexistent.action", ActionStage.PreValidation, CreateContext());

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteHooksAsync_MatchingHook_Executes()
    {
        var hook = new SampleHook();
        var executor = CreateExecutor(hook);

        var result = await executor.ExecuteHooksAsync("sale.order.confirm", ActionStage.PreValidation, CreateContext());

        result.IsSuccess.Should().BeTrue();
        hook.PreValidationCalled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteHooksAsync_StageFiltering_Only_Runs_Matching_Stage()
    {
        var hook = new SampleHook();
        var executor = CreateExecutor(hook);

        var result = await executor.ExecuteHooksAsync("sale.order.confirm", ActionStage.PostOperation, CreateContext());

        result.IsSuccess.Should().BeTrue();
        hook.PostOperationCalled.Should().BeTrue();
        hook.PreValidationCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteHooksAsync_PreValidation_Error_Blocks_Action()
    {
        var hook = new BlockingPreValHook();
        var executor = CreateExecutor(hook);

        var result = await executor.ExecuteHooksAsync("sale.order.confirm", ActionStage.PreValidation, CreateContext());

        result.IsSuccess.Should().BeFalse();
        result.Findings.Should().Contain(f => f.Severity == ActionFindingSeverity.Error);
    }

    [Fact]
    public async Task ExecuteHooksAsync_PreOperation_Error_Blocks_Action()
    {
        var hook = new BlockingPreOpHook();
        var executor = CreateExecutor(hook);

        var result = await executor.ExecuteHooksAsync("sale.order.confirm", ActionStage.PreOperation, CreateContext());

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteHooksAsync_PostOperation_Error_Does_Not_Block()
    {
        var hook = new BlockingPostOpHook();
        var executor = CreateExecutor(hook);

        var result = await executor.ExecuteHooksAsync("sale.order.confirm", ActionStage.PostOperation, CreateContext());

        result.IsSuccess.Should().BeTrue("PostOperation errors should not block");
    }

    [Fact]
    public async Task ExecuteHooksAsync_ContextTypeMismatch_Throws_ArgumentException()
    {
        var hook = new SampleHook();
        var executor = CreateExecutor(hook);

        // Pass wrong context type
        var wrongContext = new ActionContext<WrongEntity>
        {
            Entity = new WrongEntity(),
            Actor = new FakeActorContext(),
        };

        var act = () => executor.ExecuteHooksAsync("sale.order.confirm", ActionStage.PreValidation, wrongContext);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteHooksAsync_Aggregates_Findings_From_Multiple_Hooks()
    {
        var hook1 = new WarningHook1();
        var hook2 = new WarningHook2();
        var executor = CreateExecutor(hook1, hook2);

        var result = await executor.ExecuteHooksAsync("sale.order.confirm", ActionStage.PreValidation, CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Findings.Should().HaveCount(2);
        result.Findings.Should().OnlyContain(f => f.Severity == ActionFindingSeverity.Warning);
    }

    [Fact]
    public async Task ExecuteHooksAsync_Blocking_Hook_Exception_Returns_Failure()
    {
        var hook = new ThrowingHook();
        var executor = CreateExecutor(hook);

        var result = await executor.ExecuteHooksAsync("sale.order.confirm", ActionStage.PreValidation, CreateContext());

        result.IsSuccess.Should().BeFalse("exceptions in blocking hooks should fail the action");
        result.Findings.Should().ContainSingle(f => f.Message.Contains("threw an exception"));
    }

    [Fact]
    public async Task ExecuteHooksAsync_PostOp_Hook_Exception_Is_Swallowed()
    {
        var hook = new ThrowingPostOpHook();
        var executor = CreateExecutor(hook);

        var result = await executor.ExecuteHooksAsync("sale.order.confirm", ActionStage.PostOperation, CreateContext());

        result.IsSuccess.Should().BeTrue("exceptions in PostOperation hooks should be swallowed");
    }

    [Fact]
    public async Task ExecuteHooksAsync_MainOperation_Error_Does_Not_Block()
    {
        var hook = new BlockingMainOpHook();
        var executor = CreateExecutor(hook);

        var result = await executor.ExecuteHooksAsync("sale.order.confirm", ActionStage.MainOperation, CreateContext());

        result.IsSuccess.Should().BeTrue("MainOperation hooks should not block per interface contract");
    }

    private static ActionContext<TestEntity> CreateContext() =>
        new()
        {
            Entity = new TestEntity(),
            Actor = new FakeActorContext(),
        };

    private static HookExecutor CreateExecutor(params IActionHook[] hooks)
    {
        var services = new ServiceCollection();
        foreach (var hook in hooks)
        {
            services.AddSingleton<IActionHook>(hook);
        }

        var sp = services.BuildServiceProvider();
        return new HookExecutor(sp, NullLogger<HookExecutor>.Instance);
    }

    public sealed record TestEntity;

    public sealed record WrongEntity;

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

#pragma warning disable CA1822 // Hook methods must be instance methods for reflection-based discovery

    private sealed class SampleHook : IActionHook
    {
        public bool PreValidationCalled { get; private set; }

        public bool PostOperationCalled { get; private set; }

        [Hook("sale.order.confirm", ActionStage.PreValidation)]
        public Task<ActionResult> OnPreValidation(ActionContext<TestEntity> context)
        {
            PreValidationCalled = true;
            return Task.FromResult(ActionResult.Success());
        }

        [Hook("sale.order.confirm", ActionStage.PostOperation)]
        public Task<ActionResult> OnPostOperation(ActionContext<TestEntity> context)
        {
            PostOperationCalled = true;
            return Task.FromResult(ActionResult.Success());
        }
    }

    private sealed class BlockingPreValHook : IActionHook
    {
        [Hook("sale.order.confirm", ActionStage.PreValidation)]
        public Task<ActionResult> OnPreValidation(ActionContext<TestEntity> context) =>
            Task.FromResult(ActionResult.Failure("hook", "Blocked by hook"));
    }

    private sealed class BlockingPreOpHook : IActionHook
    {
        [Hook("sale.order.confirm", ActionStage.PreOperation)]
        public Task<ActionResult> OnPreValidation(ActionContext<TestEntity> context) =>
            Task.FromResult(ActionResult.Failure("hook", "Blocked by pre-op hook"));
    }

    private sealed class BlockingPostOpHook : IActionHook
    {
        [Hook("sale.order.confirm", ActionStage.PostOperation)]
        public Task<ActionResult> OnPostOperation(ActionContext<TestEntity> context) =>
            Task.FromResult(ActionResult.Failure("hook", "Post-op failure"));
    }

    private sealed class WarningHook1 : IActionHook
    {
        [Hook("sale.order.confirm", ActionStage.PreValidation)]
        public Task<ActionResult> OnPreValidation(ActionContext<TestEntity> context) =>
            Task.FromResult(ActionResult.Success(
            [
                new ActionFinding
                {
                    Severity = ActionFindingSeverity.Warning,
                    Message = "Warning from hook 1",
                },
            ]));
    }

    private sealed class WarningHook2 : IActionHook
    {
        [Hook("sale.order.confirm", ActionStage.PreValidation)]
        public Task<ActionResult> OnPreValidation(ActionContext<TestEntity> context) =>
            Task.FromResult(ActionResult.Success(
            [
                new ActionFinding
                {
                    Severity = ActionFindingSeverity.Warning,
                    Message = "Warning from hook 2",
                },
            ]));
    }

    private sealed class BlockingMainOpHook : IActionHook
    {
        [Hook("sale.order.confirm", ActionStage.MainOperation)]
        public Task<ActionResult> OnMainOp(ActionContext<TestEntity> context) =>
            Task.FromResult(ActionResult.Failure("hook", "MainOp failure"));
    }

    private sealed class ThrowingHook : IActionHook
    {
        [Hook("sale.order.confirm", ActionStage.PreValidation)]
        public Task<ActionResult> OnPreValidation(ActionContext<TestEntity> context) =>
            throw new InvalidOperationException("Unexpected error in hook");
    }

    private sealed class ThrowingPostOpHook : IActionHook
    {
        [Hook("sale.order.confirm", ActionStage.PostOperation)]
        public Task<ActionResult> OnPostOp(ActionContext<TestEntity> context) =>
            throw new InvalidOperationException("Unexpected error in post-op hook");
    }

#pragma warning restore CA1822
}
