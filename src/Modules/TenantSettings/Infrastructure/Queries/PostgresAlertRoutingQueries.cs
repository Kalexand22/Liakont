namespace Liakont.Modules.TenantSettings.Infrastructure.Queries;

using Dapper;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lecture Dapper de la matrice de routage des alertes (F12 §5.3.1, FIX212). Toujours scopée par
/// <c>company_id</c> (CLAUDE.md n°9/17), ordonnée par rang. Liste VIDE si le tenant n'a aucune entrée.
/// </summary>
public sealed class PostgresAlertRoutingQueries : IAlertRoutingQueries
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresAlertRoutingQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AlertRoutingRuleDto>> GetAlertRoutingMatrix(Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, company_id, rule_key, severity, recipients, ordinal, created_at
            FROM tenantsettings.alert_routing_rules
            WHERE company_id = @CompanyId
            ORDER BY ordinal ASC
            """;

        using var conn = await _connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync(
            new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));

        var result = new List<AlertRoutingRuleDto>();
        foreach (var row in rows)
        {
            result.Add(new AlertRoutingRuleDto
            {
                RuleKey = (string?)row.rule_key,
                Severity = (string?)row.severity,
                Recipients = TenantSettingsRowReader.ToStringList((object?)row.recipients),
                Ordinal = (int)row.ordinal,
            });
        }

        return result;
    }
}
