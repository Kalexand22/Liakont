namespace Stratum.Modules.Notification.Tests.Integration;

using System.Data;
using Dapper;
using FluentAssertions;
using Stratum.Modules.Notification.Infrastructure.Queries;
using Stratum.Modules.Notification.Tests.Integration.Fixtures;
using Xunit;

[Collection("NotificationIntegration")]
public sealed class DeliveryRecordIntegrationTests
{
    private readonly NotificationDatabaseFixture _fixture;

    public DeliveryRecordIntegrationTests(NotificationDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ListByEntity_Should_Return_Records_For_Entity()
    {
        var cf = _fixture.CreateConnectionFactory();
        var entityId = Guid.NewGuid().ToString("N")[..8];

        await InsertDeliveryRecord(cf, "reservation", entityId, slaBreached: false, failed: false);
        await InsertDeliveryRecord(cf, "reservation", entityId, slaBreached: false, failed: false);

        var queries = new PostgresDeliveryRecordQueries(cf);
        var result = await queries.ListByEntity("reservation", entityId);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(r => r.EntityType == "reservation" && r.EntityId == entityId);
    }

    [Fact]
    public async Task ListByEntity_Should_Return_Empty_When_No_Records()
    {
        var cf = _fixture.CreateConnectionFactory();
        var queries = new PostgresDeliveryRecordQueries(cf);

        var result = await queries.ListByEntity("reservation", "nonexistent_" + Guid.NewGuid().ToString("N")[..8]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListSlaBreaches_Should_Return_Only_Breached_Records()
    {
        var cf = _fixture.CreateConnectionFactory();
        var entityId = Guid.NewGuid().ToString("N")[..8];

        await InsertDeliveryRecord(cf, "reservation", entityId, slaBreached: true, failed: false);
        await InsertDeliveryRecord(cf, "reservation", entityId, slaBreached: false, failed: false);

        var queries = new PostgresDeliveryRecordQueries(cf);
        var result = await queries.ListSlaBreaches(null);

        result.Should().OnlyContain(r => r.SlaBreached);
    }

    [Fact]
    public async Task ListSlaBreaches_Should_Filter_By_CompanyId()
    {
        var cf = _fixture.CreateConnectionFactory();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var entityId = Guid.NewGuid().ToString("N")[..8];

        await InsertDeliveryRecord(cf, "reservation", entityId, slaBreached: true, failed: false, companyId: companyA);
        await InsertDeliveryRecord(cf, "reservation", entityId, slaBreached: true, failed: false, companyId: companyB);

        var queries = new PostgresDeliveryRecordQueries(cf);

        var resultA = await queries.ListSlaBreaches(companyA);
        var resultB = await queries.ListSlaBreaches(companyB);

        resultA.Should().OnlyContain(r => r.CompanyId == null || r.CompanyId == companyA);
        resultB.Should().OnlyContain(r => r.CompanyId == null || r.CompanyId == companyB);
    }

    [Fact]
    public async Task ListFailedForRetry_Should_Return_Failed_Under_Max_Retries()
    {
        var cf = _fixture.CreateConnectionFactory();
        var entityId = Guid.NewGuid().ToString("N")[..8];

        await InsertDeliveryRecord(cf, "reservation", entityId, slaBreached: false, failed: true, retryCount: 1);
        await InsertDeliveryRecord(cf, "reservation", entityId, slaBreached: false, failed: true, retryCount: 5);

        var queries = new PostgresDeliveryRecordQueries(cf);
        var result = await queries.ListFailedForRetry(3);

        result.Should().OnlyContain(r => r.RetryCount < 3);
    }

    private static async Task InsertDeliveryRecord(
        Stratum.Common.Infrastructure.Database.IConnectionFactory cf,
        string entityType,
        string entityId,
        bool slaBreached,
        bool failed,
        int retryCount = 0,
        Guid? companyId = null)
    {
        using IDbConnection conn = await cf.OpenAsync();

        const string sql = """
            INSERT INTO notification.delivery_records (id, notification_id, template_code, recipient_email, entity_type, entity_id, sent_at, failed_at, retry_count, sla_breached, company_id)
            VALUES (@Id, @NotificationId, @TemplateCode, @RecipientEmail, @EntityType, @EntityId, @SentAt, @FailedAt, @RetryCount, @SlaBreached, @CompanyId)
            """;

        await conn.ExecuteAsync(sql, new
        {
            Id = Guid.NewGuid(),
            NotificationId = (Guid?)null,
            TemplateCode = "reservation-ack",
            RecipientEmail = "test@test.local",
            EntityType = entityType,
            EntityId = entityId,
            SentAt = DateTimeOffset.UtcNow,
            FailedAt = failed ? DateTimeOffset.UtcNow : (DateTimeOffset?)null,
            RetryCount = retryCount,
            SlaBreached = slaBreached,
            CompanyId = companyId,
        });
    }
}
