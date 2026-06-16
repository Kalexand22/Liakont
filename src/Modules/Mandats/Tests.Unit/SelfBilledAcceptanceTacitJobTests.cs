namespace Liakont.Modules.Mandats.Tests.Unit;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Mandats.Infrastructure.TacitAcceptance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Jobs;
using Xunit;

/// <summary>
/// Mécanique du job de bascule tacite (MND04, ADR-0024 §4) : le travail par tenant résout le service scoped
/// (jamais sa propre boucle multi-tenant), et le handler système fait le fan-out via <c>ITenantJobRunner</c>
/// (SOL06, INV-ACCEPT-6). Gabarit DailyAnchoring.
/// </summary>
public sealed class SelfBilledAcceptanceTacitJobTests
{
    [Fact]
    public async Task TenantJob_Resolves_Scoped_Service_And_Processes()
    {
        var recording = new RecordingTacitAcceptanceService();
        using ServiceProvider provider = new ServiceCollection()
            .AddSingleton<ITacitAcceptanceService>(recording)
            .BuildServiceProvider();
        var job = new SelfBilledAcceptanceTacitJob();

        await job.ExecuteAsync(new TenantJobContext("acme", provider));

        recording.Calls.Should().Be(1);
        job.Name.Should().Be("mandats.self-billed-tacit-acceptance");
    }

    [Fact]
    public async Task FanOutHandler_Runs_TacitJob_Over_All_Tenants()
    {
        var runner = new RecordingTenantJobRunner();
        var handler = new SelfBilledAcceptanceTacitFanOutHandler(
            runner, NullLogger<SelfBilledAcceptanceTacitFanOutHandler>.Instance);

        await handler.HandleAsync(new SelfBilledAcceptanceTacitTrigger());

        runner.LastJob.Should().BeOfType<SelfBilledAcceptanceTacitJob>(
            "la bascule tacite passe EXCLUSIVEMENT par TenantJobRunner — jamais une boucle maison (INV-ACCEPT-6).");
    }

    private sealed class RecordingTacitAcceptanceService : ITacitAcceptanceService
    {
        public int Calls { get; private set; }

        public Task<TacitAcceptanceRunResult> ProcessDueAsync(CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new TacitAcceptanceRunResult(0, 0));
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
