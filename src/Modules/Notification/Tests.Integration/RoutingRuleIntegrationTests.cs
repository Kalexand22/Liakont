namespace Stratum.Modules.Notification.Tests.Integration;

using System.Data;
using Dapper;
using FluentAssertions;
using Stratum.Modules.Notification.Infrastructure.Queries;
using Stratum.Modules.Notification.Tests.Integration.Fixtures;
using Xunit;

[Collection("NotificationIntegration")]
public sealed class RoutingRuleIntegrationTests
{
    private readonly NotificationDatabaseFixture _fixture;

    public RoutingRuleIntegrationTests(NotificationDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ListByEntityType_Should_Return_Rules_Ordered_By_Priority()
    {
        var cf = _fixture.CreateConnectionFactory();
        var queries = new PostgresRoutingRuleQueries(cf);

        var result = await queries.ListByEntityType("reservation", null);

        result.Should().BeInAscendingOrder(r => r.Priority);
    }

    [Fact]
    public async Task GetByCode_Should_Return_Null_When_Not_Found()
    {
        var cf = _fixture.CreateConnectionFactory();
        var queries = new PostgresRoutingRuleQueries(cf);

        var result = await queries.GetByCode("nonexistent_rule", "reservation");

        result.Should().BeNull();
    }

    [Fact]
    public async Task Insert_And_Query_Custom_Rule_Should_Succeed()
    {
        var cf = _fixture.CreateConnectionFactory();
        var code = "test_" + Guid.NewGuid().ToString("N")[..8];

        await InsertRule(cf, code, "test-entity");

        var queries = new PostgresRoutingRuleQueries(cf);
        var result = await queries.GetByCode(code, "test-entity");

        result.Should().NotBeNull();
        result!.Code.Should().Be(code);
        result.EntityType.Should().Be("test-entity");
        result.ServiceCode.Should().Be("gestion-salles");
    }

    [Fact]
    public async Task Insert_Duplicate_Code_EntityType_Should_Throw()
    {
        var cf = _fixture.CreateConnectionFactory();
        var code = "dup_" + Guid.NewGuid().ToString("N")[..8];

        await InsertRule(cf, code, "reservation");

        var act = async () => await InsertRule(cf, code, "reservation");

        await act.Should().ThrowAsync<Npgsql.PostgresException>()
            .Where(ex => ex.SqlState == "23505");
    }

    private static async Task InsertRule(
        Stratum.Common.Infrastructure.Database.IConnectionFactory cf,
        string code,
        string entityType)
    {
        using IDbConnection conn = await cf.OpenAsync();

        const string sql = """
            INSERT INTO notification.routing_rules (id, code, name, entity_type, service_code, recipient_type, recipient_value, conditions, priority, is_active, company_id, created_at)
            VALUES (@Id, @Code, @Name, @EntityType, @ServiceCode, @RecipientType, @RecipientValue, @Conditions::jsonb, @Priority, @IsActive, @CompanyId, @CreatedAt)
            """;

        await conn.ExecuteAsync(sql, new
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = "Test Rule " + code,
            EntityType = entityType,
            ServiceCode = "gestion-salles",
            RecipientType = (short)0,
            RecipientValue = "test@test.local",
            Conditions = "[]",
            Priority = 10,
            IsActive = true,
            CompanyId = (Guid?)null,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }
}
