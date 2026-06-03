namespace Stratum.Modules.Notification.Tests.Acceptance;

using System.Data;
using System.Reflection;
using Dapper;
using DbUp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Notification.Contracts.Queries;
using Stratum.Modules.Notification.Domain.Entities;
using Stratum.Modules.Notification.Infrastructure.Queries;
using Testcontainers.PostgreSql;
using Xunit;

public sealed class WebhookSubscriptionRoundTripTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        RunMigrations();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task Create_And_Query_Webhook_Subscription_EndToEnd()
    {
        var cf = CreateConnectionFactory();
        var companyId = Guid.NewGuid();

        // 1. Create subscription entity
        var subscription = WebhookSubscription.Create(
            "Test Webhook",
            "sales.quote.created",
            "https://example.com/hook",
            "abcdefghijklmnopqrstuvwxyz0123456789",
            companyId);

        // 2. Insert into DB
        using (IDbConnection conn = await cf.OpenAsync())
        {
            const string sql = """
                INSERT INTO notification.webhook_subscriptions
                    (id, name, event_type, target_url, secret, is_active, company_id, created_at, updated_at)
                VALUES
                    (@Id, @Name, @EventType, @TargetUrl, @Secret, @IsActive, @CompanyId, @CreatedAt, @UpdatedAt)
                """;

            await conn.ExecuteAsync(sql, new
            {
                subscription.Id,
                subscription.Name,
                subscription.EventType,
                subscription.TargetUrl,
                subscription.Secret,
                subscription.IsActive,
                subscription.CompanyId,
                subscription.CreatedAt,
                subscription.UpdatedAt,
            });
        }

        // 3. Query by ID
        var queries = new PostgresWebhookQueries(cf);
        var result = await queries.GetById(subscription.Id);

        result.Should().NotBeNull();
        result!.EventType.Should().Be("sales.quote.created");
        result.TargetUrl.Should().Be("https://example.com/hook");
        result.IsActive.Should().BeTrue();
        result.CompanyId.Should().Be(companyId);

        // 4. Query by company
        var companyResults = await queries.ListByCompany(companyId);
        companyResults.Should().ContainSingle(s => s.Id == subscription.Id);

        // 5. Query by event type
        var eventResults = await queries.ListByEventType("sales.quote.created");
        eventResults.Should().ContainSingle(s => s.Id == subscription.Id);
    }

    [Fact]
    public async Task Update_Webhook_Subscription_EndToEnd()
    {
        var cf = CreateConnectionFactory();
        var companyId = Guid.NewGuid();

        // Create and insert
        var subscription = WebhookSubscription.Create(
            "Test Webhook",
            "sales.quote.created",
            "https://example.com/hook",
            "abcdefghijklmnopqrstuvwxyz0123456789",
            companyId);

        using (IDbConnection conn = await cf.OpenAsync())
        {
            const string sql = """
                INSERT INTO notification.webhook_subscriptions
                    (id, name, event_type, target_url, secret, is_active, company_id, created_at, updated_at)
                VALUES
                    (@Id, @Name, @EventType, @TargetUrl, @Secret, @IsActive, @CompanyId, @CreatedAt, @UpdatedAt)
                """;

            await conn.ExecuteAsync(sql, new
            {
                subscription.Id,
                subscription.Name,
                subscription.EventType,
                subscription.TargetUrl,
                subscription.Secret,
                subscription.IsActive,
                subscription.CompanyId,
                subscription.CreatedAt,
                subscription.UpdatedAt,
            });
        }

        // Update
        subscription.Update(
            "Test Webhook",
            "sales.quote.validated",
            "https://example.com/hook-v2",
            "zyxwvutsrqponmlkjihgfedcba0123456789",
            false);

        using (IDbConnection conn = await cf.OpenAsync())
        {
            const string sql = """
                UPDATE notification.webhook_subscriptions
                SET event_type = @EventType,
                    target_url = @TargetUrl,
                    secret = @Secret,
                    is_active = @IsActive,
                    updated_at = @UpdatedAt
                WHERE id = @Id
                """;

            await conn.ExecuteAsync(sql, new
            {
                subscription.Id,
                subscription.EventType,
                subscription.TargetUrl,
                subscription.Secret,
                subscription.IsActive,
                subscription.UpdatedAt,
            });
        }

        // Verify
        var queries = new PostgresWebhookQueries(cf);
        var result = await queries.GetById(subscription.Id);

        result.Should().NotBeNull();
        result!.EventType.Should().Be("sales.quote.validated");
        result.TargetUrl.Should().Be("https://example.com/hook-v2");
        result.IsActive.Should().BeFalse();
        result.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_Webhook_Subscription_EndToEnd()
    {
        var cf = CreateConnectionFactory();
        var companyId = Guid.NewGuid();

        var subscription = WebhookSubscription.Create(
            "Test Webhook",
            "sales.quote.created",
            "https://example.com/hook",
            "abcdefghijklmnopqrstuvwxyz0123456789",
            companyId);

        using (IDbConnection conn = await cf.OpenAsync())
        {
            const string sql = """
                INSERT INTO notification.webhook_subscriptions
                    (id, name, event_type, target_url, secret, is_active, company_id, created_at, updated_at)
                VALUES
                    (@Id, @Name, @EventType, @TargetUrl, @Secret, @IsActive, @CompanyId, @CreatedAt, @UpdatedAt)
                """;

            await conn.ExecuteAsync(sql, new
            {
                subscription.Id,
                subscription.Name,
                subscription.EventType,
                subscription.TargetUrl,
                subscription.Secret,
                subscription.IsActive,
                subscription.CompanyId,
                subscription.CreatedAt,
                subscription.UpdatedAt,
            });
        }

        // Delete
        using (IDbConnection conn = await cf.OpenAsync())
        {
            await conn.ExecuteAsync(
                "DELETE FROM notification.webhook_subscriptions WHERE id = @Id",
                new { subscription.Id });
        }

        // Verify deleted
        var queries = new PostgresWebhookQueries(cf);
        var result = await queries.GetById(subscription.Id);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetById_Returns_Null_For_NonExistent_Subscription()
    {
        var cf = CreateConnectionFactory();
        var queries = new PostgresWebhookQueries(cf);

        var result = await queries.GetById(Guid.NewGuid());

        result.Should().BeNull();
    }

    private NpgsqlConnectionFactory CreateConnectionFactory()
    {
        var options = Options.Create(new DatabaseOptions { ConnectionString = _container.GetConnectionString() });
        return new NpgsqlConnectionFactory(options);
    }

    private void RunMigrations()
    {
        var dbOptions = Options.Create(new DatabaseOptions { ConnectionString = _container.GetConnectionString() });
        var migrationOptions = Options.Create(new MigrationAssembliesOptions());
        var runner = new MigrationRunner(dbOptions, migrationOptions, NullLogger<MigrationRunner>.Instance);
        runner.MigrateUp();

        var upgrader = DeployChanges.To
            .PostgresqlDatabase(_container.GetConnectionString())
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(Infrastructure.NotificationModuleRegistration))!,
                s => s.Contains(".Migrations.", StringComparison.Ordinal))
            .JournalToPostgresqlTable("outbox", "schema_versions")
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            throw result.Error;
        }
    }
}
