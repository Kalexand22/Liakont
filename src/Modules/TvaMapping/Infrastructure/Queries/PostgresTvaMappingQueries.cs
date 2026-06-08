namespace Liakont.Modules.TvaMapping.Infrastructure.Queries;

using System.Collections.Generic;
using Dapper;
using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Liakont.Modules.TvaMapping.Contracts.Queries;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lectures Dapper de la table de mapping TVA, scopées par <c>company_id</c> (CLAUDE.md n°9/17). Le
/// chargement passe par <see cref="TvaMappingMaterializer"/> qui re-valide la structure : une table
/// persistée invalide lève une exception au chargement (item TVA01 §4) plutôt que d'être servie.
/// </summary>
public sealed class PostgresTvaMappingQueries : ITvaMappingQueries
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresTvaMappingQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<MappingTableDto?> GetMappingTable(Guid companyId, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenAsync(ct);
        var table = await TvaMappingMaterializer.LoadByCompanyAsync(connection, companyId, transaction: null, ct);
        return table is null ? null : MapTable(table);
    }

    public async Task<IReadOnlyList<MappingChangeLogEntryDto>> GetChangeLog(Guid companyId, CancellationToken ct = default)
    {
        // Lecture seule du journal append-only, du plus récent au plus ancien (company_id — CLAUDE.md n°9).
        // jsonb projeté en texte (::text) pour exposer la valeur avant/après telle quelle. AUCUNE écriture.
        const string sql = """
            SELECT id, change_type, source_regime_code, part, mapping_version,
                   before_value::text AS before_json, after_value::text AS after_json,
                   operator_id, operator_name, occurred_at
            FROM tvamapping.mapping_change_log
            WHERE company_id = @CompanyId
            ORDER BY occurred_at DESC, id DESC
            """;

        using var connection = await _connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync(
            new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));

        var entries = new List<MappingChangeLogEntryDto>();
        foreach (var row in rows)
        {
            int? partValue = row.part is null ? null : (int)row.part;
            entries.Add(new MappingChangeLogEntryDto
            {
                Id = (Guid)row.id,
                ChangeType = ((MappingChangeType)(int)row.change_type).ToString(),
                SourceRegimeCode = (string?)row.source_regime_code,
                Part = partValue is null ? null : ((MappingPart)partValue.Value).ToString(),
                MappingVersion = (string)row.mapping_version,
                BeforeJson = (string?)row.before_json,
                AfterJson = (string?)row.after_json,
                OperatorId = (Guid)row.operator_id,
                OperatorName = (string?)row.operator_name,
                OccurredAt = (DateTimeOffset)row.occurred_at,
            });
        }

        return entries;
    }

    private static MappingTableDto MapTable(MappingTable table)
    {
        var rules = new List<MappingRuleDto>(table.Rules.Count);
        foreach (var rule in table.Rules)
        {
            rules.Add(MapRule(rule));
        }

        return new MappingTableDto
        {
            Id = table.Id,
            CompanyId = table.CompanyId,
            MappingVersion = table.MappingVersion,
            ValidatedBy = table.ValidatedBy,
            ValidatedDate = table.ValidatedDate,
            IsValidated = table.IsValidated,
            DefaultBehavior = table.DefaultBehavior.ToString(),
            Rules = rules,
            CreatedAt = table.CreatedAt,
            UpdatedAt = table.UpdatedAt,
        };
    }

    private static MappingRuleDto MapRule(MappingRule rule)
    {
        return new MappingRuleDto
        {
            SourceRegimeCode = rule.SourceRegimeCode,
            Label = rule.Label,
            Part = rule.Part.ToString(),
            SourceFlags = rule.SourceFlags,
            Category = rule.Category.ToString(),
            Vatex = rule.Vatex,
            Note = rule.Note,
            RateMode = rule.RateMode.ToString(),
            RateValue = rule.RateValue,
        };
    }
}
