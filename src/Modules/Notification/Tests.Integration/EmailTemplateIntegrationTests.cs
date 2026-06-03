namespace Stratum.Modules.Notification.Tests.Integration;

using System.Data;
using Dapper;
using FluentAssertions;
using Stratum.Modules.Notification.Infrastructure.Queries;
using Stratum.Modules.Notification.Tests.Integration.Fixtures;
using Xunit;

[Collection("NotificationIntegration")]
public sealed class EmailTemplateIntegrationTests
{
    private readonly NotificationDatabaseFixture _fixture;

    public EmailTemplateIntegrationTests(NotificationDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Insert_And_Query_Template_Should_Succeed()
    {
        var cf = _fixture.CreateConnectionFactory();
        var code = "tpl_" + Guid.NewGuid().ToString("N")[..8];

        await InsertTemplate(cf, code, "en", null);

        var queries = new PostgresEmailTemplateQueries(cf);
        var result = await queries.GetByCode(code, "en", null);

        result.Should().NotBeNull();
        result!.Code.Should().Be(code);
        result.LanguageCode.Should().Be("en");
        result.SubjectTemplate.Should().Be("Subject {{key}}");
        result.BodyTemplate.Should().Be("Body {{key}}");
    }

    [Fact]
    public async Task GetByCode_Should_Return_Null_When_Not_Found()
    {
        var cf = _fixture.CreateConnectionFactory();
        var queries = new PostgresEmailTemplateQueries(cf);

        var result = await queries.GetByCode("nonexistent_" + Guid.NewGuid().ToString("N")[..8], "en", null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Insert_Duplicate_Code_Language_Company_Should_Throw()
    {
        var cf = _fixture.CreateConnectionFactory();
        var code = "dup_" + Guid.NewGuid().ToString("N")[..8];

        await InsertTemplate(cf, code, "en", null);

        var act = async () => await InsertTemplate(cf, code, "en", null);

        await act.Should().ThrowAsync<Npgsql.PostgresException>()
            .Where(ex => ex.SqlState == "23505");
    }

    [Fact]
    public async Task Same_Code_Different_Language_Should_Succeed()
    {
        var cf = _fixture.CreateConnectionFactory();
        var code = "lang_" + Guid.NewGuid().ToString("N")[..8];

        await InsertTemplate(cf, code, "en", null);
        await InsertTemplate(cf, code, "fr", null);

        var queries = new PostgresEmailTemplateQueries(cf);

        var enResult = await queries.GetByCode(code, "en", null);
        var frResult = await queries.GetByCode(code, "fr", null);

        enResult.Should().NotBeNull();
        frResult.Should().NotBeNull();
        enResult!.LanguageCode.Should().Be("en");
        frResult!.LanguageCode.Should().Be("fr");
    }

    [Fact]
    public async Task Same_Code_Different_Company_Should_Succeed()
    {
        var cf = _fixture.CreateConnectionFactory();
        var code = "comp_" + Guid.NewGuid().ToString("N")[..8];
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        await InsertTemplate(cf, code, "en", companyA);
        await InsertTemplate(cf, code, "en", companyB);

        var queries = new PostgresEmailTemplateQueries(cf);

        var resultA = await queries.GetByCode(code, "en", companyA);
        var resultB = await queries.GetByCode(code, "en", companyB);

        resultA.Should().NotBeNull();
        resultB.Should().NotBeNull();
        resultA!.CompanyId.Should().Be(companyA);
        resultB!.CompanyId.Should().Be(companyB);
    }

    [Fact]
    public async Task List_Should_Return_All_Templates()
    {
        var cf = _fixture.CreateConnectionFactory();
        var code1 = "lst1_" + Guid.NewGuid().ToString("N")[..8];
        var code2 = "lst2_" + Guid.NewGuid().ToString("N")[..8];

        await InsertTemplate(cf, code1, "en", null);
        await InsertTemplate(cf, code2, "en", null);

        var queries = new PostgresEmailTemplateQueries(cf);
        var result = await queries.List();

        result.Should().Contain(t => t.Code == code1);
        result.Should().Contain(t => t.Code == code2);
    }

    private static async Task InsertTemplate(
        Stratum.Common.Infrastructure.Database.IConnectionFactory cf,
        string code,
        string languageCode,
        Guid? companyId)
    {
        using IDbConnection conn = await cf.OpenAsync();

        const string sql = """
            INSERT INTO notification.email_templates (id, code, subject_template, body_template, language_code, company_id, created_at)
            VALUES (@Id, @Code, @SubjectTemplate, @BodyTemplate, @LanguageCode, @CompanyId, @CreatedAt)
            """;

        await conn.ExecuteAsync(sql, new
        {
            Id = Guid.NewGuid(),
            Code = code,
            SubjectTemplate = "Subject {{key}}",
            BodyTemplate = "Body {{key}}",
            LanguageCode = languageCode,
            CompanyId = companyId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }
}
