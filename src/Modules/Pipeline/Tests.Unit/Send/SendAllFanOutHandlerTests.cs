namespace Liakont.Modules.Pipeline.Tests.Unit.Send;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Pipeline.Contracts.Jobs;
using Liakont.Modules.Pipeline.Infrastructure.Send;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Jobs;
using Xunit;

/// <summary>
/// Le handler de fan-out du SEND délègue au <see cref="ITenantJobRunner"/> (SOL06) — UNE exécution par
/// tenant actif, AUCUNE boucle multi-tenant locale (CLAUDE.md n°9 ; acceptance PIP01c).
/// </summary>
public sealed class SendAllFanOutHandlerTests
{
    [Fact]
    public async Task HandleAsync_Delegates_To_TenantJobRunner_With_A_SendTenantJob()
    {
        var runner = new CapturingTenantJobRunner();
        var handler = new SendAllFanOutHandler(runner, NullLogger<SendAllFanOutHandler>.Instance);

        await handler.HandleAsync(new SendAllTrigger());

        runner.LastJob.Should().BeOfType<SendTenantJob>("le fan-out exécute le job SEND une fois par tenant via le runner.");
        runner.LastJob!.Name.Should().Be("pipeline.send");
    }

    private sealed class CapturingTenantJobRunner : ITenantJobRunner
    {
        public ITenantJob? LastJob { get; private set; }

        public Task<TenantJobRunSummary> RunForAllTenantsAsync(ITenantJob job, CancellationToken cancellationToken = default)
        {
            LastJob = job;
            return Task.FromResult(new TenantJobRunSummary(job.Name, 0, 0, new List<TenantJobFailure>()));
        }
    }
}
