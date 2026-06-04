namespace Liakont.Modules.TvaMapping.Infrastructure;

using System.Data;
using Dapper;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.TvaMapping.Domain.Entities;

/// <summary>
/// Charge une table de mapping TVA (en-tête + règles) depuis la base et la reconstitue en entité de
/// domaine. La reconstitution re-valide la structure (item TVA01 §4) : une table persistée corrompue
/// lève <see cref="Domain.InvalidMappingTableException"/> au chargement plutôt que d'être servie
/// fausse. Lecture scopée par <c>company_id</c> (CLAUDE.md n°9).
/// </summary>
internal static class TvaMappingMaterializer
{
    private const string SelectTableSql = """
        SELECT id, company_id, mapping_version, validated_by, validated_date,
               default_behavior, created_at, updated_at
        FROM tvamapping.mapping_tables
        WHERE company_id = @CompanyId
        """;

    private const string SelectRulesSql = """
        SELECT ordinal, source_regime_code, label, part, source_flags,
               category, vatex, note, rate_mode, rate_value
        FROM tvamapping.mapping_rules
        WHERE table_id = @TableId
        ORDER BY ordinal ASC
        """;

    public static async Task<MappingTable?> LoadByCompanyAsync(
        IDbConnection connection,
        Guid companyId,
        IDbTransaction? transaction,
        CancellationToken ct)
    {
        var tableRow = await connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(SelectTableSql, new { CompanyId = companyId }, transaction, cancellationToken: ct));

        if (tableRow is null)
        {
            return null;
        }

        var tableId = (Guid)tableRow.id;

        var ruleRows = await connection.QueryAsync(
            new CommandDefinition(SelectRulesSql, new { TableId = tableId }, transaction, cancellationToken: ct));

        var rules = new List<MappingRule>();
        foreach (var row in ruleRows)
        {
            rules.Add(MapRule(row));
        }

        return MappingTable.Reconstitute(
            tableId,
            (Guid)tableRow.company_id,
            (string)tableRow.mapping_version,
            (string?)tableRow.validated_by,
            TvaMappingRowReader.ToNullableDateOnly((object?)tableRow.validated_date),
            (MappingDefaultBehavior)(int)tableRow.default_behavior,
            rules,
            TvaMappingRowReader.ToDateTimeOffset((object)tableRow.created_at),
            TvaMappingRowReader.ToNullableDateTimeOffset((object?)tableRow.updated_at));
    }

    private static MappingRule MapRule(dynamic row)
    {
        return new MappingRule
        {
            SourceRegimeCode = (string)row.source_regime_code,
            Label = (string?)row.label,
            Part = (MappingPart)(int)row.part,
            SourceFlags = TvaMappingRowReader.ToSourceFlags((object?)row.source_flags),
            Category = (VatCategory)(int)row.category,
            Vatex = (string?)row.vatex,
            Note = (string?)row.note,
            RateMode = (RateMode)(int)row.rate_mode,
            RateValue = (decimal?)row.rate_value,
        };
    }
}
