namespace Stratum.Common.Infrastructure.Tests.Integration;

using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.BlobStorage;
using Stratum.Common.Infrastructure.CrossTenant;
using Stratum.Common.Testing;
using Xunit;

public sealed class CrossTenantPublisherTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public CrossTenantPublisherTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PublishAsync_Should_Insert_Event_With_Pending_Status()
    {
        // Arrange
        var factory = _fixture.CreateConnectionFactory();
        var publisher = new CrossTenantPublisher(factory, NullLogger<CrossTenantPublisher>.Instance);

        var payload = new { Title = "Test Event", Description = "A test cross-tenant event" };

        // Act
        await publisher.PublishAsync(
            sourceTenant: "tenant-a",
            targetTenant: "tenant-b",
            eventType: "Test.Ping.Created",
            payload: payload);

        // Assert
        using var conn = await factory.OpenAsync();
        var row = await conn.QuerySingleAsync(
            """
            SELECT source_tenant, target_tenant, event_type, payload, status, retry_count
            FROM outbox.cross_tenant_events
            WHERE source_tenant = 'tenant-a' AND target_tenant = 'tenant-b' AND event_type = 'Test.Ping.Created'
            ORDER BY created_at DESC LIMIT 1
            """);

        ((string)row.source_tenant).Should().Be("tenant-a");
        ((string)row.target_tenant).Should().Be("tenant-b");
        ((string)row.event_type).Should().Be("Test.Ping.Created");
        ((string)row.status).Should().Be("pending");
        ((int)row.retry_count).Should().Be(0);

        var payloadJson = JsonDocument.Parse((string)row.payload);
        payloadJson.RootElement.GetProperty("title").GetString().Should().Be("Test Event");
        payloadJson.RootElement.GetProperty("description").GetString().Should().Be("A test cross-tenant event");
    }

    [Fact]
    public async Task PublishAsync_Should_Serialize_BlobRefs_When_Present()
    {
        // Arrange
        var factory = _fixture.CreateConnectionFactory();
        var publisher = new CrossTenantPublisher(factory, NullLogger<CrossTenantPublisher>.Instance);

        var blobs = new List<BlobReference>
        {
            new("key-1", "doc.pdf", "application/pdf", 1024),
            new("key-2", "img.png", "image/png", 2048),
        };

        // Act
        await publisher.PublishAsync(
            sourceTenant: "tenant-a",
            targetTenant: "tenant-c",
            eventType: "Test.Document.Attached",
            payload: new { DocId = 42 },
            blobs: blobs);

        // Assert
        using var conn = await factory.OpenAsync();
        var blobRefsJson = await conn.ExecuteScalarAsync<string>(
            """
            SELECT blob_refs::text
            FROM outbox.cross_tenant_events
            WHERE source_tenant = 'tenant-a' AND target_tenant = 'tenant-c' AND event_type = 'Test.Document.Attached'
            ORDER BY created_at DESC LIMIT 1
            """);

        blobRefsJson.Should().NotBeNull();
        var blobRefs = JsonDocument.Parse(blobRefsJson!);
        blobRefs.RootElement.GetArrayLength().Should().Be(2);
        blobRefs.RootElement[0].GetProperty("storageKey").GetString().Should().Be("key-1");
        blobRefs.RootElement[1].GetProperty("storageKey").GetString().Should().Be("key-2");
    }

    [Fact]
    public async Task PublishAsync_Should_Store_Null_BlobRefs_When_Not_Provided()
    {
        // Arrange
        var factory = _fixture.CreateConnectionFactory();
        var publisher = new CrossTenantPublisher(factory, NullLogger<CrossTenantPublisher>.Instance);

        // Act
        await publisher.PublishAsync(
            sourceTenant: "tenant-x",
            targetTenant: "tenant-y",
            eventType: "Test.Ping.Sent",
            payload: new { Message = "no blobs" });

        // Assert
        using var conn = await factory.OpenAsync();
        var blobRefs = await conn.ExecuteScalarAsync<string>(
            """
            SELECT blob_refs::text
            FROM outbox.cross_tenant_events
            WHERE source_tenant = 'tenant-x' AND target_tenant = 'tenant-y'
            ORDER BY created_at DESC LIMIT 1
            """);

        blobRefs.Should().BeNull();
    }

    [Fact]
    public async Task PublishAsync_Public_Submission_Should_Require_SubmitterEmail()
    {
        // Arrange
        var factory = _fixture.CreateConnectionFactory();
        var publisher = new CrossTenantPublisher(factory, NullLogger<CrossTenantPublisher>.Instance);

        // Act
        var act = () => publisher.PublishAsync(
            sourceTenant: null,
            targetTenant: "tenant-b",
            eventType: "Test.Ping.Submitted",
            payload: new { Data = "test" },
            submitterEmail: null);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("submitterEmail");
    }

    [Fact]
    public async Task PublishAsync_Public_Submission_With_Email_Should_Succeed()
    {
        // Arrange
        var factory = _fixture.CreateConnectionFactory();
        var publisher = new CrossTenantPublisher(factory, NullLogger<CrossTenantPublisher>.Instance);

        // Act
        await publisher.PublishAsync(
            sourceTenant: null,
            targetTenant: "tenant-pub",
            eventType: "Test.Request.Submitted",
            payload: new { Name = "John" },
            submitterEmail: "john@example.com");

        // Assert
        using var conn = await factory.OpenAsync();
        var row = await conn.QuerySingleAsync(
            """
            SELECT source_tenant, target_tenant, submitter_email, status
            FROM outbox.cross_tenant_events
            WHERE target_tenant = 'tenant-pub' AND event_type = 'Test.Request.Submitted'
            ORDER BY created_at DESC LIMIT 1
            """);

        ((object)row.source_tenant).Should().BeNull();
        ((string)row.submitter_email).Should().Be("john@example.com");
        ((string)row.status).Should().Be("pending");
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("only.two")]
    [InlineData("four.segment.event.type")]
    [InlineData("lower.case.event")]
    [InlineData("Module.lower.Event")]
    [InlineData("")]
    public async Task PublishAsync_Should_Reject_Invalid_EventType_Format(string badEventType)
    {
        // Arrange
        var factory = _fixture.CreateConnectionFactory();
        var publisher = new CrossTenantPublisher(factory, NullLogger<CrossTenantPublisher>.Instance);

        // Act
        var act = () => publisher.PublishAsync(
            sourceTenant: "tenant-a",
            targetTenant: "tenant-b",
            eventType: badEventType,
            payload: new { });

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("Module.Aggregate.Verb")]
    [InlineData("Evenementiel.Demande.Created")]
    [InlineData("Test.Ping.Sent")]
    public async Task PublishAsync_Should_Accept_Valid_EventType_Format(string goodEventType)
    {
        // Arrange
        var factory = _fixture.CreateConnectionFactory();
        var publisher = new CrossTenantPublisher(factory, NullLogger<CrossTenantPublisher>.Instance);

        // Act
        var act = () => publisher.PublishAsync(
            sourceTenant: "tenant-valid",
            targetTenant: "tenant-check",
            eventType: goodEventType,
            payload: new { Ok = true });

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_Should_Reject_Null_TargetTenant()
    {
        var factory = _fixture.CreateConnectionFactory();
        var publisher = new CrossTenantPublisher(factory, NullLogger<CrossTenantPublisher>.Instance);

        var act = () => publisher.PublishAsync(
            sourceTenant: "tenant-a",
            targetTenant: null!,
            eventType: "Test.Ping.Sent",
            payload: new { });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PublishAsync_Should_Generate_Unique_Ids()
    {
        // Arrange
        var factory = _fixture.CreateConnectionFactory();
        var publisher = new CrossTenantPublisher(factory, NullLogger<CrossTenantPublisher>.Instance);

        // Act
        await publisher.PublishAsync("tenant-a", "tenant-uid1", "Test.Unique.First", new { N = 1 });
        await publisher.PublishAsync("tenant-a", "tenant-uid2", "Test.Unique.Second", new { N = 2 });

        // Assert
        using var conn = await factory.OpenAsync();
        var ids = (await conn.QueryAsync<Guid>(
            """
            SELECT id FROM outbox.cross_tenant_events
            WHERE source_tenant = 'tenant-a' AND event_type LIKE 'Test.Unique.%'
            """)).ToList();

        ids.Should().HaveCount(2);
        ids[0].Should().NotBe(ids[1]);
    }
}
