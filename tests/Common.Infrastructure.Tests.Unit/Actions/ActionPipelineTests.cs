namespace Stratum.Common.Infrastructure.Tests.Unit.Actions;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Actions;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Infrastructure.Actions;
using Xunit;

public sealed class ActionPipelineTests
{
    [Fact]
    public async Task ExecuteAsync_EmptyPipeline_Returns_Success()
    {
        var pipeline = CreatePipeline();

        var result = await pipeline.ExecuteAsync(CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_Steps_Execute_In_Stage_Then_Order()
    {
        var executionOrder = new List<string>();

        var steps = new IActionStep<TestEntity>[]
        {
            new TrackingStep("PostOp-10", ActionStage.PostOperation, 10, executionOrder),
            new TrackingStep("PreVal-0", ActionStage.PreValidation, 0, executionOrder),
            new TrackingStep("PreOp-5", ActionStage.PreOperation, 5, executionOrder),
            new TrackingStep("PreVal-1", ActionStage.PreValidation, 1, executionOrder),
            new TrackingStep("MainOp-0", ActionStage.MainOperation, 0, executionOrder),
        };

        var pipeline = CreatePipeline(steps);

        var result = await pipeline.ExecuteAsync(CreateContext());

        result.IsSuccess.Should().BeTrue();
        executionOrder.Should().ContainInOrder(
            "PreVal-0",
            "PreVal-1",
            "PreOp-5",
            "MainOp-0",
            "PostOp-10");
    }

    [Fact]
    public async Task ExecuteAsync_Stops_On_Error_Finding()
    {
        var executionOrder = new List<string>();

        var steps = new IActionStep<TestEntity>[]
        {
            new TrackingStep("step1", ActionStage.PreValidation, 0, executionOrder),
            new FailingStep("step2-fail", ActionStage.PreValidation, 1),
            new TrackingStep("step3-skipped", ActionStage.PreOperation, 0, executionOrder),
        };

        var pipeline = CreatePipeline(steps);

        var result = await pipeline.ExecuteAsync(CreateContext());

        result.IsSuccess.Should().BeFalse();
        result.Findings.Should().ContainSingle(f => f.Severity == ActionFindingSeverity.Error);
        executionOrder.Should().ContainSingle().Which.Should().Be("step1");
    }

    [Fact]
    public async Task ExecuteAsync_Warnings_Do_Not_Stop_Pipeline()
    {
        var executionOrder = new List<string>();

        var steps = new IActionStep<TestEntity>[]
        {
            new WarningStep("warning-step", ActionStage.PreValidation, 0),
            new TrackingStep("after-warning", ActionStage.PreOperation, 0, executionOrder),
        };

        var pipeline = CreatePipeline(steps);

        var result = await pipeline.ExecuteAsync(CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Findings.Should().ContainSingle(f => f.Severity == ActionFindingSeverity.Warning);
        executionOrder.Should().ContainSingle().Which.Should().Be("after-warning");
    }

    [Fact]
    public async Task ExecuteAsync_Info_Findings_Do_Not_Stop_Pipeline()
    {
        var steps = new IActionStep<TestEntity>[]
        {
            new InfoStep("info-step", ActionStage.PreValidation, 0),
            new TrackingStep("after-info", ActionStage.PreOperation, 0, []),
        };

        var pipeline = CreatePipeline(steps);

        var result = await pipeline.ExecuteAsync(CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Findings.Should().ContainSingle(f => f.Severity == ActionFindingSeverity.Info);
    }

    [Fact]
    public async Task ExecuteAsync_Aggregates_Findings_From_All_Steps()
    {
        var steps = new IActionStep<TestEntity>[]
        {
            new WarningStep("warn1", ActionStage.PreValidation, 0),
            new InfoStep("info1", ActionStage.PreOperation, 0),
        };

        var pipeline = CreatePipeline(steps);

        var result = await pipeline.ExecuteAsync(CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Findings.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_Respects_Cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var steps = new IActionStep<TestEntity>[]
        {
            new TrackingStep("should-not-run", ActionStage.PreValidation, 0, []),
        };

        var pipeline = CreatePipeline(steps);
        var context = new ActionContext<TestEntity>
        {
            Entity = new TestEntity(),
            Actor = new FakeActorContext(),
            CancellationToken = cts.Token,
        };

        var act = () => pipeline.ExecuteAsync(context);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_Error_Result_Includes_Prior_Findings()
    {
        var steps = new IActionStep<TestEntity>[]
        {
            new WarningStep("warn-first", ActionStage.PreValidation, 0),
            new FailingStep("fail-second", ActionStage.PreValidation, 1),
        };

        var pipeline = CreatePipeline(steps);

        var result = await pipeline.ExecuteAsync(CreateContext());

        result.IsSuccess.Should().BeFalse();
        result.Findings.Should().HaveCount(2);
        result.Findings[0].Severity.Should().Be(ActionFindingSeverity.Warning);
        result.Findings[1].Severity.Should().Be(ActionFindingSeverity.Error);
    }

    private static ActionContext<TestEntity> CreateContext() =>
        new()
        {
            Entity = new TestEntity(),
            Actor = new FakeActorContext(),
        };

    private static IActionPipeline CreatePipeline(params IActionStep<TestEntity>[] steps)
    {
        var services = new ServiceCollection();
        foreach (var step in steps)
        {
            services.AddSingleton<IActionStep<TestEntity>>(step);
        }

        services.AddScoped<IActionPipeline, ActionPipeline>();
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IActionPipeline>();
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

    private sealed class TrackingStep(
        string name,
        ActionStage stage,
        int order,
        List<string> executionOrder)
        : IActionStep<TestEntity>
    {
        public ActionStage Stage => stage;

        public int Order => order;

        public Task<ActionResult> ExecuteAsync(ActionContext<TestEntity> context)
        {
            executionOrder.Add(name);
            return Task.FromResult(ActionResult.Success());
        }
    }

    private sealed class FailingStep(string name, ActionStage stage, int order)
        : IActionStep<TestEntity>
    {
        public ActionStage Stage => stage;

        public int Order => order;

        public Task<ActionResult> ExecuteAsync(ActionContext<TestEntity> context) =>
            Task.FromResult(ActionResult.Failure(name, $"Error from {name}"));
    }

    private sealed class WarningStep(string name, ActionStage stage, int order)
        : IActionStep<TestEntity>
    {
        public ActionStage Stage => stage;

        public int Order => order;

        public Task<ActionResult> ExecuteAsync(ActionContext<TestEntity> context) =>
            Task.FromResult(ActionResult.Success(
            [
                new ActionFinding
                {
                    Severity = ActionFindingSeverity.Warning,
                    Message = $"Warning from {name}",
                },
            ]));
    }

    private sealed class InfoStep(string name, ActionStage stage, int order)
        : IActionStep<TestEntity>
    {
        public ActionStage Stage => stage;

        public int Order => order;

        public Task<ActionResult> ExecuteAsync(ActionContext<TestEntity> context) =>
            Task.FromResult(ActionResult.Success(
            [
                new ActionFinding
                {
                    Severity = ActionFindingSeverity.Info,
                    Message = $"Info from {name}",
                },
            ]));
    }
}
