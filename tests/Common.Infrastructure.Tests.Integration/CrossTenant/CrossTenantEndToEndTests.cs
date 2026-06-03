namespace Stratum.Common.Infrastructure.Tests.Integration.CrossTenant;

using System.Diagnostics;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Abstractions.CrossTenant;
using Stratum.Common.Infrastructure.CrossTenant;
using Stratum.Common.Infrastructure.CrossTenant.TestPing;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Tests.Integration.Portal;
using Xunit;

/// <summary>
/// End-to-end integration tests proving the full cross-tenant dispatch pipeline:
/// publish → dispatcher poll → handler delivery → InboundPing in target tenant DB.
/// Uses Testcontainers with system + 2 tenant databases.
/// </summary>
public sealed class CrossTenantEndToEndTests : IClassFixture<CrossTenantEndToEndFixture>, IAsyncLifetime
{
    private readonly CrossTenantEndToEndFixture _fixture;

    public CrossTenantEndToEndTests(CrossTenantEndToEndFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.CleanInboundPingsAsync(MultiTenantFixture.TenantA);
        await _fixture.CleanInboundPingsAsync(MultiTenantFixture.TenantB);
        await _fixture.CleanOutboxEventsAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Tenant_To_Tenant_Delivery_Should_Create_InboundPing()
    {
        // Arrange
        var publisher = _fixture.CreatePublisher();
        const string message = "hello from tenant-a";

        // Act — publish from tenant-a to tenant-b
        await publisher.PublishAsync(
            sourceTenant: MultiTenantFixture.TenantA,
            targetTenant: MultiTenantFixture.TenantB,
            eventType: "Test.Ping.Sent",
            payload: new PingPayload(message));

        // Run one dispatch cycle
        await _fixture.RunDispatchCycleAsync();

        // Assert — InboundPing should exist in tenant-b
        var ping = await _fixture.GetLatestInboundPingAsync(MultiTenantFixture.TenantB);
        ping.Should().NotBeNull();
        ping!.SourceTenant.Should().Be(MultiTenantFixture.TenantA);
        ping.Message.Should().Be(message);
        ping.SubmitterEmail.Should().BeNull();

        // Outbox event should be marked as delivered
        var status = await _fixture.GetEventStatusAsync("Test.Ping.Sent", MultiTenantFixture.TenantB);
        status.Should().Be("delivered");
    }

    [Fact]
    public async Task Public_Submission_Should_Create_InboundPing_With_Null_SourceTenant()
    {
        // Arrange
        var publisher = _fixture.CreatePublisher();
        const string email = "visitor@example.com";

        // Act — public submission (no source tenant)
        await publisher.PublishAsync(
            sourceTenant: null,
            targetTenant: MultiTenantFixture.TenantB,
            eventType: "Test.Ping.Sent",
            payload: new PingPayload("public ping"),
            submitterEmail: email);

        await _fixture.RunDispatchCycleAsync();

        // Assert
        var ping = await _fixture.GetLatestInboundPingAsync(MultiTenantFixture.TenantB);
        ping.Should().NotBeNull();
        ping!.SourceTenant.Should().BeNull();
        ping.Message.Should().Be("public ping");
        ping.SubmitterEmail.Should().Be(email);
    }

    [Fact]
    public async Task Idempotent_Delivery_Should_Not_Duplicate_InboundPing()
    {
        // Arrange — publish a single event
        var publisher = _fixture.CreatePublisher();
        await publisher.PublishAsync(
            sourceTenant: MultiTenantFixture.TenantA,
            targetTenant: MultiTenantFixture.TenantB,
            eventType: "Test.Ping.Sent",
            payload: new PingPayload("idempotent test"));

        // Act — dispatch twice (simulates re-delivery)
        await _fixture.RunDispatchCycleAsync();

        // Reset the event status to pending so it gets picked up again
        await _fixture.ResetEventStatusToPendingAsync("Test.Ping.Sent", MultiTenantFixture.TenantB);
        await _fixture.RunDispatchCycleAsync();

        // Assert — only one InboundPing should exist (ON CONFLICT DO NOTHING)
        var count = await _fixture.CountInboundPingsAsync(MultiTenantFixture.TenantB);
        count.Should().Be(1);
    }

    [Fact]
    public async Task End_To_End_Latency_Should_Be_Under_10_Seconds()
    {
        // Arrange
        var publisher = _fixture.CreatePublisher();
        var sw = Stopwatch.StartNew();

        // Act
        await publisher.PublishAsync(
            sourceTenant: MultiTenantFixture.TenantA,
            targetTenant: MultiTenantFixture.TenantB,
            eventType: "Test.Ping.Sent",
            payload: new PingPayload("latency test"));

        await _fixture.RunDispatchCycleAsync();
        sw.Stop();

        // Assert — entire publish + dispatch cycle should be fast
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));

        // Verify delivery happened
        var ping = await _fixture.GetLatestInboundPingAsync(MultiTenantFixture.TenantB);
        ping.Should().NotBeNull();
    }

    [Fact]
    public async Task Multiple_Events_To_Different_Tenants_Should_All_Deliver()
    {
        // Arrange
        var publisher = _fixture.CreatePublisher();

        // Publish to both tenants
        await publisher.PublishAsync(
            sourceTenant: MultiTenantFixture.TenantB,
            targetTenant: MultiTenantFixture.TenantA,
            eventType: "Test.Ping.Sent",
            payload: new PingPayload("ping to A"));

        await publisher.PublishAsync(
            sourceTenant: MultiTenantFixture.TenantA,
            targetTenant: MultiTenantFixture.TenantB,
            eventType: "Test.Ping.Sent",
            payload: new PingPayload("ping to B"));

        // Act
        await _fixture.RunDispatchCycleAsync();

        // Assert — both tenants received their ping
        var pingA = await _fixture.GetLatestInboundPingAsync(MultiTenantFixture.TenantA);
        pingA.Should().NotBeNull();
        pingA!.Message.Should().Be("ping to A");

        var pingB = await _fixture.GetLatestInboundPingAsync(MultiTenantFixture.TenantB);
        pingB.Should().NotBeNull();
        pingB!.Message.Should().Be("ping to B");
    }
}
