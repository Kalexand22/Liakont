namespace Stratum.Modules.Notification.Tests.Integration;

using System.Data;
using Dapper;
using FluentAssertions;
using Stratum.Modules.Notification.Infrastructure.Queries;
using Stratum.Modules.Notification.Tests.Integration.Fixtures;
using Xunit;

[Collection("NotificationIntegration")]
public sealed class WebhookSubscriptionIntegrationTests
{
    private readonly NotificationDatabaseFixture _fixture;

    public WebhookSubscriptionIntegrationTests(NotificationDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Insert_And_Query_Subscription_Should_Succeed()
    {
        var cf = _fixture.CreateConnectionFactory();
        var companyId = Guid.NewGuid();
        var id = await InsertSubscription(cf, "test.event", "https://example.com/hook", companyId);

        var queries = new PostgresWebhookQueries(cf);
        var result = await queries.GetById(id);

        result.Should().NotBeNull();
        result!.EventType.Should().Be("test.event");
        result.TargetUrl.Should().Be("https://example.com/hook");
        result.IsActive.Should().BeTrue();
        result.CompanyId.Should().Be(companyId);
    }

    [Fact]
    public async Task ListByEventType_Should_Return_Active_Only()
    {
        var cf = _fixture.CreateConnectionFactory();
        var companyId = Guid.NewGuid();
        var eventType = "list_evt_" + Guid.NewGuid().ToString("N")[..8];

        await InsertSubscription(cf, eventType, "https://example.com/hook1", companyId);
        await InsertSubscription(cf, eventType, "https://example.com/hook2", companyId, isActive: false);

        var queries = new PostgresWebhookQueries(cf);
        var result = await queries.ListByEventType(eventType);

        result.Should().HaveCount(1);
        result[0].TargetUrl.Should().Be("https://example.com/hook1");
    }

    [Fact]
    public async Task ListByCompany_Should_Return_All_For_Company()
    {
        var cf = _fixture.CreateConnectionFactory();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        await InsertSubscription(cf, "evt.a", "https://example.com/a", companyId);
        await InsertSubscription(cf, "evt.b", "https://example.com/b", companyId);
        await InsertSubscription(cf, "evt.c", "https://example.com/c", otherCompanyId);

        var queries = new PostgresWebhookQueries(cf);
        var result = await queries.ListByCompany(companyId);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetById_Should_Return_Null_When_Not_Found()
    {
        var cf = _fixture.CreateConnectionFactory();
        var queries = new PostgresWebhookQueries(cf);

        var result = await queries.GetById(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task Short_Secret_Should_Violate_Check_Constraint()
    {
        var cf = _fixture.CreateConnectionFactory();

        var act = async () =>
        {
            using IDbConnection conn = await cf.OpenAsync();
            const string sql = """
                INSERT INTO notification.webhook_subscriptions (id, name, event_type, target_url, secret, is_active, company_id, created_at)
                VALUES (@Id, @Name, @EventType, @TargetUrl, @Secret, true, @CompanyId, now())
                """;

            await conn.ExecuteAsync(sql, new
            {
                Id = Guid.NewGuid(),
                Name = "Short secret webhook",
                EventType = "test",
                TargetUrl = "https://example.com",
                Secret = "short",
                CompanyId = Guid.NewGuid(),
            });
        };

        await act.Should().ThrowAsync<Npgsql.PostgresException>()
            .Where(ex => ex.SqlState == "23514"); // check_violation
    }

    [Fact]
    public async Task Delete_Should_Remove_Subscription()
    {
        var cf = _fixture.CreateConnectionFactory();
        var companyId = Guid.NewGuid();
        var id = await InsertSubscription(cf, "del.event", "https://example.com/del", companyId);

        using IDbConnection conn = await cf.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM notification.webhook_subscriptions WHERE id = @Id", new { Id = id });

        var queries = new PostgresWebhookQueries(cf);
        var result = await queries.GetById(id);
        result.Should().BeNull();
    }

    private static async Task<Guid> InsertSubscription(
        Stratum.Common.Infrastructure.Database.IConnectionFactory cf,
        string eventType,
        string targetUrl,
        Guid companyId,
        bool isActive = true)
    {
        using IDbConnection conn = await cf.OpenAsync();
        var id = Guid.NewGuid();
        var secret = "abcdefghijklmnopqrstuvwxyz0123456789";

        const string sql = """
            INSERT INTO notification.webhook_subscriptions (id, name, event_type, target_url, secret, is_active, company_id, created_at)
            VALUES (@Id, @Name, @EventType, @TargetUrl, @Secret, @IsActive, @CompanyId, now())
            """;

        await conn.ExecuteAsync(sql, new
        {
            Id = id,
            Name = $"Webhook {eventType}",
            EventType = eventType,
            TargetUrl = targetUrl,
            Secret = secret,
            IsActive = isActive,
            CompanyId = companyId,
        });

        return id;
    }
}
