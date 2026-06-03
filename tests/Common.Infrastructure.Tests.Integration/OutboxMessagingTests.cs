namespace Stratum.Common.Infrastructure.Tests.Integration;

using System.Collections.Concurrent;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Outbox;
using Stratum.Common.Testing;
using Xunit;

public sealed class OutboxMessagingTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public OutboxMessagingTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FullFlowShouldPublishToOutboxAndDispatchToHandler()
    {
        // Arrange
        var receivedEvents = new ConcurrentBag<IntegrationEvent<TestEventPayload>>();
        var dispatcher = new TestEventDispatcher<TestEventPayload>(receivedEvents);
        var registry = new EventTypeRegistry();
        registry.Register<TestEventPayload>("test.messaging.flow");

        var factory = _fixture.CreateConnectionFactory();
        var writer = new OutboxWriter(NullLogger<OutboxWriter>.Instance);

        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var integrationEvent = new IntegrationEvent<TestEventPayload>
        {
            EventId = eventId,
            EventType = "test.messaging.flow",
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = correlationId,
            ModuleSource = "TestModule",
            Payload = new TestEventPayload { Message = "hello from outbox" },
            Version = 1,
        };

        // Act: write event to outbox within a transaction
        await using (var scope = await TransactionScope.BeginAsync(factory))
        {
            await writer.WriteAsync(scope, integrationEvent);
            await scope.CommitAsync();
        }

        // Act: run worker to process the batch
        var worker = CreateWorker(factory, dispatcher, registry);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StartAsync(cts.Token);

        // Wait for the worker to fully process the event (dispatch + mark processed_at)
        var deadline = DateTime.UtcNow.AddSeconds(10);
        DateTime? processedAt = null;
        while (DateTime.UtcNow < deadline)
        {
            if (!receivedEvents.IsEmpty)
            {
                using var conn = await factory.OpenAsync();
                processedAt = await conn.ExecuteScalarAsync<DateTime?>(
                    "SELECT processed_at FROM outbox.pending_events WHERE id = @Id",
                    new { Id = eventId });

                if (processedAt.HasValue)
                {
                    break;
                }
            }

            await Task.Delay(100);
        }

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        // Assert: handler received the event with correct fields
        receivedEvents.Should().ContainSingle();
        var received = receivedEvents.First();
        received.EventId.Should().Be(eventId);
        received.EventType.Should().Be("test.messaging.flow");
        received.CorrelationId.Should().Be(correlationId);
        received.ModuleSource.Should().Be("TestModule");
        received.Payload.Message.Should().Be("hello from outbox");
        received.Version.Should().Be(1);

        // Assert: outbox row is marked as processed
        processedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DuplicateEventIdShouldBeHandledIdempotently()
    {
        // Arrange
        var receivedEvents = new ConcurrentBag<IntegrationEvent<TestEventPayload>>();
        var dispatcher = new TestEventDispatcher<TestEventPayload>(receivedEvents);
        var registry = new EventTypeRegistry();
        registry.Register<TestEventPayload>("test.idempotency");

        var factory = _fixture.CreateConnectionFactory();
        var writer = new OutboxWriter(NullLogger<OutboxWriter>.Instance);

        var eventId = Guid.NewGuid();
        var integrationEvent = new IntegrationEvent<TestEventPayload>
        {
            EventId = eventId,
            EventType = "test.idempotency",
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid(),
            ModuleSource = "TestModule",
            Payload = new TestEventPayload { Message = "idempotency check" },
            Version = 1,
        };

        // Write event to outbox
        await using (var scope = await TransactionScope.BeginAsync(factory))
        {
            await writer.WriteAsync(scope, integrationEvent);
            await scope.CommitAsync();
        }

        // Act: first worker run — processes the event
        var worker = CreateWorker(factory, dispatcher, registry);

        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            await worker.StartAsync(cts.Token);

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (receivedEvents.IsEmpty && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            cts.Cancel();
            await worker.StopAsync(CancellationToken.None);
        }

        receivedEvents.Should().ContainSingle("worker should dispatch the event exactly once");
        var countAfterFirstRun = receivedEvents.Count;

        // Act: second worker run — event already processed, should not dispatch again
        var worker2 = CreateWorker(factory, dispatcher, registry);

        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
        {
            await worker2.StartAsync(cts.Token);
            await Task.Delay(TimeSpan.FromSeconds(1));
            cts.Cancel();
            await worker2.StopAsync(CancellationToken.None);
        }

        // Assert: no additional events dispatched
        receivedEvents.Count.Should().Be(
            countAfterFirstRun,
            "processed events must not be dispatched again");
    }

    private static OutboxWorker CreateWorker(
        ISystemConnectionFactory factory,
        IEventDispatcher dispatcher,
        EventTypeRegistry registry)
    {
        var workerOptions = Options.Create(new OutboxWorkerOptions
        {
            PollingInterval = TimeSpan.FromMilliseconds(100),
            BatchSize = 10,
        });

        return new OutboxWorker(
            factory,
            dispatcher,
            registry,
            workerOptions,
            NullLogger<OutboxWorker>.Instance);
    }

    private sealed record TestEventPayload
    {
        public string Message { get; init; } = string.Empty;
    }

    /// <summary>
    /// Test dispatcher that captures published events without requiring a DI container.
    /// </summary>
    private sealed class TestEventDispatcher<TPayload> : IEventDispatcher
    {
        private readonly ConcurrentBag<IntegrationEvent<TPayload>> _received;

        public TestEventDispatcher(
            ConcurrentBag<IntegrationEvent<TPayload>> received)
        {
            _received = received;
        }

        public Task PublishAsync<T>(
            IntegrationEvent<T> integrationEvent,
            CancellationToken cancellationToken = default)
        {
            if (integrationEvent is IntegrationEvent<TPayload> typed)
            {
                _received.Add(typed);
            }

            return Task.CompletedTask;
        }
    }
}
