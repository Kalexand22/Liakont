namespace Stratum.Modules.Notification.Tests.Acceptance;

using System.Data;
using System.Reflection;
using Dapper;
using DbUp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Notification.Domain.Entities;
using Stratum.Modules.Notification.Domain.Services;
using Stratum.Modules.Notification.Infrastructure.Queries;
using Testcontainers.PostgreSql;
using Xunit;

public sealed class EmailTemplateRoundTripTests : IAsyncLifetime
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
    public async Task Create_Query_And_Render_Template_EndToEnd()
    {
        var cf = CreateConnectionFactory();
        var code = "acc_" + Guid.NewGuid().ToString("N")[..8];

        // 1. Create template entity
        var template = EmailTemplate.Create(
            code,
            "Welcome {{name}}",
            "Hello {{name}}, your order {{orderId}} is ready.",
            "en",
            null);

        // 2. Insert into DB
        using (IDbConnection conn = await cf.OpenAsync())
        {
            const string sql = """
                INSERT INTO notification.email_templates (id, code, subject_template, body_template, language_code, company_id, created_at)
                VALUES (@Id, @Code, @SubjectTemplate, @BodyTemplate, @LanguageCode, @CompanyId, @CreatedAt)
                """;

            await conn.ExecuteAsync(sql, new
            {
                template.Id,
                template.Code,
                template.SubjectTemplate,
                template.BodyTemplate,
                template.LanguageCode,
                template.CompanyId,
                template.CreatedAt,
            });
        }

        // 3. Query back
        var queries = new PostgresEmailTemplateQueries(cf);
        var result = await queries.GetByCode(code, "en", null);

        result.Should().NotBeNull();
        result!.Code.Should().Be(code);
        result.SubjectTemplate.Should().Be("Welcome {{name}}");

        // 4. Render template with placeholders
        var placeholders = new Dictionary<string, string>
        {
            ["name"] = "Alice",
            ["orderId"] = "ORD-001",
        };

        var renderedSubject = TemplateRenderer.Render(result.SubjectTemplate, placeholders);
        var renderedBody = TemplateRenderer.Render(result.BodyTemplate, placeholders);

        renderedSubject.Should().Be("Welcome Alice");
        renderedBody.Should().Be("Hello Alice, your order ORD-001 is ready.");
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
