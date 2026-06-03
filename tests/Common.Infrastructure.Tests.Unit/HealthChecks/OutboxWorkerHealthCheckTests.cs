namespace Stratum.Common.Infrastructure.HealthChecks;

using System.Data;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Outbox;
using Xunit;

public class OutboxWorkerHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_Should_ReturnDegraded_When_NoWorkerRegistered()
    {
        var check = new OutboxWorkerHealthCheck([]);
        var result = await check.CheckHealthAsync(MakeContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("not registered");
    }

    [Fact]
    public async Task CheckHealthAsync_Should_ReturnDegraded_When_OtherServicesButNoWorker()
    {
        var check = new OutboxWorkerHealthCheck([new FakeHostedService()]);
        var result = await check.CheckHealthAsync(MakeContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("not registered");
    }

    [Fact]
    public async Task CheckHealthAsync_Should_ReturnDegraded_When_WorkerNotStarted()
    {
        var worker = MakeWorker();
        var check = new OutboxWorkerHealthCheck([worker]);

        var result = await check.CheckHealthAsync(MakeContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("not started");
    }

    [Fact]
    public async Task CheckHealthAsync_Should_ReturnHealthy_When_WorkerIsRunning()
    {
        var worker = MakeWorker();
        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        var check = new OutboxWorkerHealthCheck([worker]);
        var result = await check.CheckHealthAsync(MakeContext());

        result.Status.Should().Be(HealthStatus.Healthy);

        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CheckHealthAsync_Should_ReturnDegraded_When_WorkerStopped()
    {
        var worker = MakeWorker();
        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        var check = new OutboxWorkerHealthCheck([worker]);
        var result = await check.CheckHealthAsync(MakeContext());

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    private static HealthCheckContext MakeContext()
        => new()
        {
            Registration = new HealthCheckRegistration("outbox", _ => null!, HealthStatus.Unhealthy, []),
        };

    private static OutboxWorker MakeWorker()
        => new(
            new BlockingConnectionFactory(),
            new NullEventDispatcher(),
            new NullEventTypeRegistry(),
            Options.Create(new OutboxWorkerOptions { PollingInterval = TimeSpan.FromMilliseconds(50) }),
            NullLogger<OutboxWorker>.Instance);

    // ── Fakes ──────────────────────────────────────────────────────────────
    private sealed class FakeHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Blocks forever on OpenAsync so the worker polling loop stays alive for the Healthy test.
    /// </summary>
    private sealed class BlockingConnectionFactory : ISystemConnectionFactory
    {
        public async Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null!; // never reached
        }
    }

    private sealed class NullEventDispatcher : IEventDispatcher
    {
        public Task PublishAsync<TPayload>(IntegrationEvent<TPayload> integrationEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NullEventTypeRegistry : IEventTypeRegistry
    {
        public IEventTypeRegistry Register<TPayload>(string eventType)
            => this;

        public Type? GetPayloadType(string eventType)
            => null;
    }
}
