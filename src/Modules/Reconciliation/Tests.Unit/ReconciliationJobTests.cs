namespace Liakont.Modules.Reconciliation.Tests.Unit;

using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Reconciliation.Contracts;
using Liakont.Modules.Reconciliation.Infrastructure;
using Liakont.Modules.Reconciliation.Tests.Unit.Doubles;
using Stratum.Common.Abstractions.Jobs;
using Xunit;

public sealed class ReconciliationJobTests
{
    [Fact]
    public async Task TenantJob_ResolvesServiceFromScope_AndRunsPass()
    {
        var service = new RecordingReconciliationService();
        var provider = new SingleServiceProvider(typeof(IReconciliationService), service);
        var context = new TenantJobContext("tenant-x", provider);
        var job = new ReconciliationTenantJob();

        await job.ExecuteAsync(context);

        service.RunCount.Should().Be(1);
        job.Name.Should().Be("trk.reconciliation");
    }

    [Fact]
    public async Task FanOutHandler_RunsReconciliationTenantJobForAllTenants()
    {
        var runner = new RecordingTenantJobRunner();
        var handler = new ReconciliationFanOutJobHandler(runner);

        await handler.HandleAsync(new ReconciliationFanOutJobPayload());

        runner.LastJob.Should().BeOfType<ReconciliationTenantJob>();
        runner.LastJob!.Name.Should().Be("trk.reconciliation");
    }
}
