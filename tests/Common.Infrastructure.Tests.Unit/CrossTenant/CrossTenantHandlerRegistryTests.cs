namespace Stratum.Common.Infrastructure.Tests.Unit.CrossTenant;

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.CrossTenant;
using Stratum.Common.Infrastructure.CrossTenant;
using Xunit;

public class CrossTenantHandlerRegistryTests
{
    private static readonly ILogger<CrossTenantHandlerRegistry> Logger =
        NullLoggerFactory.Instance.CreateLogger<CrossTenantHandlerRegistry>();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void Resolve_returns_handler_for_registered_event_type()
    {
        var handler = new FakePingHandler();
        var registration = WrapHandler(handler);
        var registry = new CrossTenantHandlerRegistry([registration], Logger);

        var resolved = registry.Resolve("Test.Ping.Sent");

        Assert.NotNull(resolved);
        Assert.Equal("Test.Ping.Sent", resolved.EventType);
    }

    [Fact]
    public void Resolve_returns_null_for_unregistered_event_type()
    {
        var registry = new CrossTenantHandlerRegistry([], Logger);

        var resolved = registry.Resolve("Unknown.Event.Type");

        Assert.Null(resolved);
    }

    [Fact]
    public void Resolve_keeps_first_registration_on_conflict()
    {
        var handler1 = new FakePingHandler();
        var handler2 = new AnotherPingHandler();
        var reg1 = WrapHandler(handler1);
        var reg2 = WrapHandler(handler2);

        var registry = new CrossTenantHandlerRegistry([reg1, reg2], Logger);

        var resolved = registry.Resolve("Test.Ping.Sent");
        Assert.NotNull(resolved);
    }

    [Fact]
    public async Task Resolved_handler_deserializes_payload_and_calls_typed_handler()
    {
        var handler = new FakePingHandler();
        var registration = WrapHandler(handler);
        var registry = new CrossTenantHandlerRegistry([registration], Logger);

        var resolved = registry.Resolve("Test.Ping.Sent")!;

        var payload = JsonSerializer.SerializeToElement(new PingPayload("hello"), JsonOptions);

        var envelope = new CrossTenantEnvelope(
            Guid.NewGuid(),
            "tenant-a",
            "tenant-b",
            "Test.Ping.Sent",
            payload,
            null,
            null,
            DateTimeOffset.UtcNow);

        await resolved.HandleAsync(envelope, payload, CancellationToken.None);

        Assert.True(handler.WasCalled);
        Assert.Equal("hello", handler.ReceivedMessage);
    }

    [Fact]
    public void Multiple_different_event_types_can_be_registered()
    {
        var pingHandler = new FakePingHandler();
        var pongHandler = new FakePongHandler();
        var reg1 = WrapHandler(pingHandler);
        var reg2 = WrapHandler(pongHandler);

        var registry = new CrossTenantHandlerRegistry([reg1, reg2], Logger);

        Assert.NotNull(registry.Resolve("Test.Ping.Sent"));
        Assert.NotNull(registry.Resolve("Test.Pong.Received"));
        Assert.Null(registry.Resolve("Test.Other.Missing"));
    }

    [Fact]
    public void AddCrossTenantHandlers_registers_handler_via_assembly_scanning()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCrossTenantHandlers(typeof(FakePingHandler).Assembly);

        var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<ICrossTenantHandlerRegistry>();

        var resolved = registry.Resolve("Test.Ping.Sent");
        Assert.NotNull(resolved);
    }

    private static CrossTenantHandlerRegistry.HandlerRegistration WrapHandler<TPayload>(
        ICrossTenantHandler<TPayload> handler)
    {
        var adapter = new CrossTenantHandlerRegistry.JsonElementAdapter<TPayload>(handler);
        return new CrossTenantHandlerRegistry.HandlerRegistration(handler.EventType, adapter);
    }

    public sealed record PingPayload(string Message);

    public sealed record PongPayload(string Reply);

    public sealed class FakePingHandler : ICrossTenantHandler<PingPayload>
    {
        public bool WasCalled { get; private set; }

        public string? ReceivedMessage { get; private set; }

        public string EventType => "Test.Ping.Sent";

        public Task HandleAsync(CrossTenantEnvelope envelope, PingPayload payload, CancellationToken ct)
        {
            WasCalled = true;
            ReceivedMessage = payload.Message;
            return Task.CompletedTask;
        }
    }

    public sealed class AnotherPingHandler : ICrossTenantHandler<PingPayload>
    {
        public string EventType => "Test.Ping.Sent";

        public Task HandleAsync(CrossTenantEnvelope envelope, PingPayload payload, CancellationToken ct)
            => Task.CompletedTask;
    }

    public sealed class FakePongHandler : ICrossTenantHandler<PongPayload>
    {
        public string EventType => "Test.Pong.Received";

        public Task HandleAsync(CrossTenantEnvelope envelope, PongPayload payload, CancellationToken ct)
            => Task.CompletedTask;
    }
}
