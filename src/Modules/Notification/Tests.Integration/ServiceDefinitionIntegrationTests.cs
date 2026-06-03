namespace Stratum.Modules.Notification.Tests.Integration;

using System.Data;
using Dapper;
using FluentAssertions;
using Stratum.Modules.Notification.Infrastructure.Queries;
using Stratum.Modules.Notification.Tests.Integration.Fixtures;
using Xunit;

[Collection("NotificationIntegration")]
public sealed class ServiceDefinitionIntegrationTests
{
    private readonly NotificationDatabaseFixture _fixture;

    public ServiceDefinitionIntegrationTests(NotificationDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task List_Should_Return_Services_Ordered_By_Code()
    {
        var cf = _fixture.CreateConnectionFactory();
        var queries = new PostgresServiceDefinitionQueries(cf);

        var result = await queries.List(null);

        result.Should().BeInAscendingOrder(s => s.Code);
    }

    [Fact]
    public async Task GetByCode_Should_Return_Null_When_Not_Found()
    {
        var cf = _fixture.CreateConnectionFactory();
        var queries = new PostgresServiceDefinitionQueries(cf);

        var result = await queries.GetByCode("nonexistent_service", null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Insert_And_Query_Custom_Service_Should_Succeed()
    {
        var cf = _fixture.CreateConnectionFactory();
        var code = "svc_" + Guid.NewGuid().ToString("N")[..8];

        await InsertService(cf, code, null);

        var queries = new PostgresServiceDefinitionQueries(cf);
        var result = await queries.GetByCode(code, null);

        result.Should().NotBeNull();
        result!.Code.Should().Be(code);
        result.Email.Should().Be($"{code}@test.local");
    }

    [Fact]
    public async Task Insert_Duplicate_Code_Should_Throw()
    {
        var cf = _fixture.CreateConnectionFactory();
        var code = "dup_" + Guid.NewGuid().ToString("N")[..8];

        await InsertService(cf, code, null);

        var act = async () => await InsertService(cf, code, null);

        await act.Should().ThrowAsync<Npgsql.PostgresException>()
            .Where(ex => ex.SqlState == "23505");
    }

    [Fact]
    public async Task Same_Code_Different_Company_Should_Succeed()
    {
        var cf = _fixture.CreateConnectionFactory();
        var code = "comp_" + Guid.NewGuid().ToString("N")[..8];
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        await InsertService(cf, code, companyA);
        await InsertService(cf, code, companyB);

        var queries = new PostgresServiceDefinitionQueries(cf);

        var resultA = await queries.GetByCode(code, companyA);
        var resultB = await queries.GetByCode(code, companyB);

        resultA.Should().NotBeNull();
        resultB.Should().NotBeNull();
    }

    private static async Task InsertService(
        Stratum.Common.Infrastructure.Database.IConnectionFactory cf,
        string code,
        Guid? companyId)
    {
        using IDbConnection conn = await cf.OpenAsync();

        const string sql = """
            INSERT INTO notification.service_definitions (id, code, name, email, description, is_active, company_id, created_at)
            VALUES (@Id, @Code, @Name, @Email, @Description, @IsActive, @CompanyId, @CreatedAt)
            """;

        await conn.ExecuteAsync(sql, new
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = "Test Service " + code,
            Email = $"{code}@test.local",
            Description = "Test service",
            IsActive = true,
            CompanyId = companyId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }
}
