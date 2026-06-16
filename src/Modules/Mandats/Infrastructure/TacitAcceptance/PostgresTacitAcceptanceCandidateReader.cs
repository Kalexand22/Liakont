namespace Liakont.Modules.Mandats.Infrastructure.TacitAcceptance;

using Dapper;
using Liakont.Modules.Mandats.Domain.Entities;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lecture Dapper des acceptations dues à la bascule tacite (MND04) dans la base du tenant courant. Le
/// pré-filtre SQL reflète exactement la condition du job (ADR-0024 §4) : <c>state = PendingAcceptance</c>,
/// <c>deadline_utc IS NOT NULL</c> (≡ mandat écrit ET délai non null, encodé à la création), <c>deadline_utc
/// ≤ @NowUtc</c>. L'index partiel <c>ix_self_billed_acceptances_due_tacit</c> (V007) couvre ce prédicat.
/// On ne retourne QUE les clés : l'agrégat complet est rechargé sous verrou (<c>FOR UPDATE</c>) par le
/// service, qui re-vérifie l'éligibilité avant de transiter (anti-TOCTOU : l'état peut avoir changé entre
/// l'énumération et le verrou).
/// </summary>
internal sealed class PostgresTacitAcceptanceCandidateReader : ITacitAcceptanceCandidateReader
{
    private const string SelectDueSql = """
        SELECT company_id, document_id
        FROM mandats.self_billed_acceptances
        WHERE state = @PendingState
          AND deadline_utc IS NOT NULL
          AND deadline_utc <= @NowUtc
        ORDER BY deadline_utc
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresTacitAcceptanceCandidateReader(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<TacitAcceptanceCandidate>> ListDueAsync(
        DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync(
            new CommandDefinition(
                SelectDueSql,
                new { PendingState = (int)SelfBilledAcceptanceState.PendingAcceptance, NowUtc = nowUtc },
                cancellationToken: ct));

        var result = new List<TacitAcceptanceCandidate>();
        foreach (var row in rows)
        {
            result.Add(new TacitAcceptanceCandidate((Guid)row.company_id, (Guid)row.document_id));
        }

        return result;
    }
}
