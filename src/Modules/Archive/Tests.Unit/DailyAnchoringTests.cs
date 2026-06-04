namespace Liakont.Modules.Archive.Tests.Unit;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Jobs;
using Xunit;

/// <summary>Tests du job d'ancrage quotidien (TRK06) : travail par tenant + fan-out via le runner (SOL06).</summary>
public sealed class DailyAnchoringTests
{
    [Fact]
    public async Task TenantJob_ResolvesScopedService_AndAnchors()
    {
        var recording = new RecordingAnchoringService();
        using ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IArchiveAnchoringService>(recording)
            .BuildServiceProvider();
        var job = new DailyAnchoringTenantJob();

        await job.ExecuteAsync(new TenantJobContext("acme", provider));

        recording.Calls.Should().Be(1);
        job.Name.Should().Be("archive.daily-anchoring");
    }

    [Fact]
    public async Task FanOutHandler_RunsDailyAnchoringJob_OverAllTenants()
    {
        var runner = new RecordingTenantJobRunner();
        var handler = new DailyAnchoringFanOutHandler(runner, NullLogger<DailyAnchoringFanOutHandler>.Instance);

        await handler.HandleAsync(new DailyAnchoringTrigger());

        runner.LastJob.Should().BeOfType<DailyAnchoringTenantJob>();
    }

    private sealed class RecordingAnchoringService : IArchiveAnchoringService
    {
        public int Calls { get; private set; }

        public Task<AnchoringOutcome> AnchorChainHeadAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new AnchoringOutcome(AnchoringStatus.NothingToAnchor, "test", null));
        }
    }

    private sealed class RecordingTenantJobRunner : ITenantJobRunner
    {
        public ITenantJob? LastJob { get; private set; }

        public Task<TenantJobRunSummary> RunForAllTenantsAsync(ITenantJob job, CancellationToken cancellationToken = default)
        {
            LastJob = job;
            return Task.FromResult(new TenantJobRunSummary(job.Name, 1, 1, new List<TenantJobFailure>()));
        }
    }
}
