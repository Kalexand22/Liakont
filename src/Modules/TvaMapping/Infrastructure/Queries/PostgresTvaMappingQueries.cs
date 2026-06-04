namespace Liakont.Modules.TvaMapping.Infrastructure.Queries;

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
