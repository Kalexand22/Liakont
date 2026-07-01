namespace Liakont.Modules.Ged.Infrastructure.Graph;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Ged.Application.Graph;
using Liakont.Modules.Ged.Domain.Graph;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lecture Dapper des règles d'inférence/héritage ACTIVES (<c>ged_catalog.relation_inference_rules</c>), résolue
/// sur la base DU TENANT (isolation = la connexion, F19 §3.2 ; GED24). Les colonnes <c>snake_case</c> sont
/// ALIASÉES (Dapper n'active pas <c>MatchNamesWithUnderscores</c> dans ce dépôt — sans alias la propriété
/// resterait silencieusement vide). Chaque ligne est validée vers le Domain via le constructeur
/// <see cref="RelationInferenceRule"/> (mode/borne contrôlés) — une ligne hors contrat lève (jamais deviner).
/// </summary>
internal sealed class PostgresRelationInferenceRuleStore : IRelationInferenceRuleStore
{
    private const string ActiveRulesSql = """
        SELECT relation_kind AS RelationKind,
               mode          AS Mode,
               max_depth     AS MaxDepth
        FROM ged_catalog.relation_inference_rules
        WHERE is_active = true
        ORDER BY relation_kind, mode
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresRelationInferenceRuleStore(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<RelationInferenceRule>> GetActiveRulesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<RuleRow>(new CommandDefinition(
            ActiveRulesSql,
            cancellationToken: cancellationToken));

        return rows
            .Select(r => new RelationInferenceRule(r.RelationKind, r.Mode, r.MaxDepth))
            .ToList();
    }

    private sealed class RuleRow
    {
        public string RelationKind { get; set; } = string.Empty;

        public string Mode { get; set; } = string.Empty;

        public int MaxDepth { get; set; }
    }
}
