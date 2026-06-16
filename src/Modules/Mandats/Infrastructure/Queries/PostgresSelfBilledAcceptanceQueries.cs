namespace Liakont.Modules.Mandats.Infrastructure.Queries;

using Dapper;
using Liakont.Modules.Mandats.Contracts.DTOs;
using Liakont.Modules.Mandats.Contracts.Queries;
using Liakont.Modules.Mandats.Domain.Entities;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Requêtes de lecture (seules) de l'acceptation des auto-factures sous mandat sur PostgreSQL. Toutes scopées
/// par <c>company_id</c> (CLAUDE.md n°9/17, INV-MANDATS-1) — aucune lecture cross-tenant. L'état est exposé
/// via l'agrégat de domaine (re-construit par <see cref="SelfBilledAcceptanceMaterializer"/>) pour exposer
/// <see cref="SelfBilledAcceptance.IsAccepted"/> sans dupliquer la règle.
/// </summary>
internal sealed class PostgresSelfBilledAcceptanceQueries : ISelfBilledAcceptanceQueries
{
    private const string SelectLogSql = """
        SELECT document_id, from_state, to_state, operator_id, operator_name, occurred_at
        FROM mandats.self_billed_acceptance_log
        WHERE company_id = @CompanyId AND document_id = @DocumentId
        ORDER BY occurred_at DESC
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresSelfBilledAcceptanceQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<SelfBilledAcceptanceDto?> GetAcceptance(Guid companyId, Guid documentId, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenAsync(ct);
        var acceptance = await SelfBilledAcceptanceMaterializer.LoadAsync(connection, companyId, documentId, null, ct);
        return acceptance is null ? null : Map(acceptance);
    }

    public async Task<IReadOnlyList<SelfBilledAcceptanceLogEntryDto>> GetAcceptanceLog(
        Guid companyId, Guid documentId, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync(
            new CommandDefinition(SelectLogSql, new { CompanyId = companyId, DocumentId = documentId }, cancellationToken: ct));

        var result = new List<SelfBilledAcceptanceLogEntryDto>();
        foreach (var row in rows)
        {
            result.Add(new SelfBilledAcceptanceLogEntryDto
            {
                DocumentId = (Guid)row.document_id,
                FromState = StateName((int?)row.from_state),
                ToState = ((SelfBilledAcceptanceState)(int)row.to_state).ToString(),
                OperatorId = (Guid?)row.operator_id,
                OperatorName = (string?)row.operator_name,
                OccurredAt = MandatsRowReader.ToDateTimeOffset((object)row.occurred_at),
            });
        }

        return result;
    }

    private static string? StateName(int? state)
        => state is null ? null : ((SelfBilledAcceptanceState)state.Value).ToString();

    private static SelfBilledAcceptanceDto Map(SelfBilledAcceptance acceptance)
        => new()
        {
            DocumentId = acceptance.DocumentId,
            State = acceptance.State.ToString(),
            AllocatedNumber = acceptance.AllocatedNumber,
            PendingSince = acceptance.PendingSince,
            DeadlineUtc = acceptance.DeadlineUtc,
            IsAccepted = acceptance.IsAccepted,
        };
}
