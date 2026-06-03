namespace Stratum.Modules.Notification.Infrastructure.Queries;

using System.Data;
using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;
using Stratum.Modules.Notification.Infrastructure.Services;

public sealed class PostgresEmailTemplateQueries : IEmailTemplateQueries
{
    private static readonly string[] CategoryNames = ["transactional", "routing", "escalation", "reminder"];

    private readonly IConnectionFactory _connectionFactory;

    public PostgresEmailTemplateQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<EmailTemplateDto?> GetByCode(string code, string languageCode, Guid? companyId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, code, subject_template, body_template, language_code, category, entity_type, template_links, company_id, created_at, updated_at
            FROM notification.email_templates
            WHERE code = @Code
              AND language_code = @LanguageCode
              AND ((company_id IS NULL AND @CompanyId IS NULL) OR company_id = @CompanyId)
            """;

        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Code = code, LanguageCode = languageCode, CompanyId = companyId }, cancellationToken: ct));

        return row is null ? null : MapDto(row);
    }

    public async Task<EmailTemplateDto?> GetById(Guid templateId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, code, subject_template, body_template, language_code, category, entity_type, template_links, company_id, created_at, updated_at
            FROM notification.email_templates
            WHERE id = @Id
            """;

        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = templateId }, cancellationToken: ct));

        return row is null ? null : MapDto(row);
    }

    public async Task<IReadOnlyList<EmailTemplateDto>> List(Guid? companyId = null, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, code, subject_template, body_template, language_code, category, entity_type, template_links, company_id, created_at, updated_at
            FROM notification.email_templates
            WHERE ((company_id IS NULL AND @CompanyId IS NULL) OR company_id = @CompanyId)
            ORDER BY code, language_code
            """;

        var rows = await conn.QueryAsync(
            new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));

        var result = new List<EmailTemplateDto>();
        foreach (var r in rows)
        {
            result.Add(MapDto(r));
        }

        return result;
    }

    private static EmailTemplateDto MapDto(dynamic row)
    {
        int cat = (int)(short)row.category;
        var links = TemplateLinkSerializer.Deserialize((string)row.template_links);

        return new EmailTemplateDto
        {
            Id = (Guid)row.id,
            Code = (string)row.code,
            SubjectTemplate = (string)row.subject_template,
            BodyTemplate = (string)row.body_template,
            LanguageCode = ((string)row.language_code).Trim(),
            Category = cat >= 0 && cat < CategoryNames.Length ? CategoryNames[cat] : cat.ToString(System.Globalization.CultureInfo.InvariantCulture),
            EntityType = (string?)row.entity_type,
            TemplateLinks = links.Select(l => new TemplateLinkDto { Label = l.Label, UrlTemplate = l.UrlTemplate }).ToList(),
            CompanyId = (Guid?)row.company_id,
            CreatedAt = (DateTimeOffset)row.created_at,
            UpdatedAt = (DateTimeOffset?)row.updated_at,
        };
    }
}
