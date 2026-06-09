namespace Liakont.Modules.Supervision.Tests.Unit;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Infrastructure;
using Liakont.Modules.Supervision.Tests.Unit.Doubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.Jobs;
using Xunit;

public sealed class SupervisionJobTests
{
    [Fact]
    public async Task TenantJob_Resolves_Engine_From_Scope_And_Evaluates()
    {
        var engine = new RecordingAlertEvaluationService();
        var provider = new SingleServiceProvider(typeof(IAlertEvaluationService), engine);
        var context = new TenantJobContext("tenant-x", provider);
        var job = new SupervisionEvaluationTenantJob();

        await job.ExecuteAsync(context);

        engine.EvaluateCount.Should().Be(1);
        engine.LastTenantId.Should().Be("tenant-x");
        job.Name.Should().Be("sup.evaluation");
    }

    [Fact]
    public async Task TenantJob_Throws_When_A_Rule_Failed()
    {
        var resultWithFailure = new AlertEvaluationResult(0, [new RuleEvaluationFailure("agent.mute", "boom")]);
        var engine = new RecordingAlertEvaluationService(resultWithFailure);
        var provider = new SingleServiceProvider(typeof(IAlertEvaluationService), engine);
        var context = new TenantJobContext("tenant-x", provider);
        var job = new SupervisionEvaluationTenantJob();

        var act = async () => await job.ExecuteAsync(context);

        // Le job lève pour que le runner isole l'échec sur ce tenant et le remonte (jamais silencieux).
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("agent.mute");
    }

    [Fact]
    public async Task FanOutHandler_Runs_The_TenantJob_For_All_Tenants()
    {
        var runner = new RecordingTenantJobRunner();
        var handler = new SupervisionEvaluationFanOutHandler(
            runner, NullLogger<SupervisionEvaluationFanOutHandler>.Instance);

        await handler.HandleAsync(new SupervisionEvaluationTrigger());

        runner.LastJob.Should().BeOfType<SupervisionEvaluationTenantJob>();
        runner.LastJob!.Name.Should().Be("sup.evaluation");
    }

    [Fact]
    public async Task DigestTenantJob_Resolves_Sender_From_Scope_And_Sends()
    {
        var sender = new RecordingAlertDigestSender();
        var provider = new SingleServiceProvider(typeof(IAlertDigestSender), sender);
        var context = new TenantJobContext("tenant-x", provider);
        var job = new SupervisionDigestTenantJob();

        await job.ExecuteAsync(context);

        sender.CallCount.Should().Be(1);
        sender.LastTenantId.Should().Be("tenant-x");
        job.Name.Should().Be("sup.digest");
    }

    [Fact]
    public async Task DigestFanOutHandler_Runs_The_DigestTenantJob_When_Enabled()
    {
        var runner = new RecordingTenantJobRunner();
        var handler = new SupervisionDigestFanOutHandler(
            runner,
            Options.Create(new SupervisionNotificationOptions { DailyDigestEnabled = true }),
            NullLogger<SupervisionDigestFanOutHandler>.Instance);

        await handler.HandleAsync(new SupervisionDigestTrigger());

        runner.LastJob.Should().BeOfType<SupervisionDigestTenantJob>();
        runner.LastJob!.Name.Should().Be("sup.digest");
    }

    [Fact]
    public async Task DigestFanOutHandler_Skips_FanOut_When_Disabled()
    {
        var runner = new RecordingTenantJobRunner();
        var handler = new SupervisionDigestFanOutHandler(
            runner,
            Options.Create(new SupervisionNotificationOptions { DailyDigestEnabled = false }),
            NullLogger<SupervisionDigestFanOutHandler>.Instance);

        await handler.HandleAsync(new SupervisionDigestTrigger());

        // Désactivé : aucun fan-out (pas de scope par tenant pour rien).
        runner.LastJob.Should().BeNull();
    }
}
