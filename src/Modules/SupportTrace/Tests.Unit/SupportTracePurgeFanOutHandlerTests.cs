namespace Liakont.Modules.SupportTrace.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.SupportTrace.Infrastructure;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Jobs;
using Xunit;

/// <summary>
/// Le handler de purge fait le fan-out de <see cref="SupportTracePurgeTenantJob"/> sur tous les tenants, et
/// les échecs PAR TENANT (isolés par le runner) sont JOURNALISÉS, jamais avalés silencieusement ni propagés
/// en exception (la purge d'un tenant ne doit pas arrêter les autres ni faire échouer le job système).
/// </summary>
public sealed class SupportTracePurgeFanOutHandlerTests
{
    [Fact]
    public async Task Runs_The_Purge_Job_Over_All_Tenants()
    {
        var runner = new RecordingTenantJobRunner(new TenantJobRunSummary("support-trace.purge", 2, 2, []));
        var logger = new CapturingLogger<SupportTracePurgeFanOutHandler>();
        var handler = new SupportTracePurgeFanOutHandler(runner, logger);

        await handler.HandleAsync(new SupportTracePurgeTrigger());

        runner.LastJob.Should().BeOfType<SupportTracePurgeTenantJob>();
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Warning, "aucun échec : pas d'avertissement");
    }

    [Fact]
    public async Task Logs_Each_Tenant_Failure_Without_Throwing()
    {
        var summary = new TenantJobRunSummary(
            "support-trace.purge",
            totalTenants: 2,
            succeededCount: 1,
            new List<TenantJobFailure> { new("tenant-b", "boom") });
        var runner = new RecordingTenantJobRunner(summary);
        var logger = new CapturingLogger<SupportTracePurgeFanOutHandler>();
        var handler = new SupportTracePurgeFanOutHandler(runner, logger);

        var act = async () => await handler.HandleAsync(new SupportTracePurgeTrigger());

        await act.Should().NotThrowAsync("un échec par tenant est isolé, jamais propagé");
        logger.Entries.Should().Contain(
            e => e.Level == LogLevel.Warning && e.Message.Contains("tenant-b") && e.Message.Contains("boom"),
            "l'échec d'un tenant est journalisé, jamais avalé en silence");
    }

    private sealed class RecordingTenantJobRunner : ITenantJobRunner
    {
        private readonly TenantJobRunSummary _summary;

        public RecordingTenantJobRunner(TenantJobRunSummary summary) => _summary = summary;

        public ITenantJob? LastJob { get; private set; }

        public Task<TenantJobRunSummary> RunForAllTenantsAsync(ITenantJob job, CancellationToken cancellationToken = default)
        {
            LastJob = job;
            return Task.FromResult(_summary);
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}
