namespace Liakont.Modules.Mandats.Infrastructure;

using System.Data;
using Dapper;

/// <summary>
/// Lit la <b>companion fiscale</b> d'une acceptation self-billed (<c>mandats.self_billed_acceptances</c>, SIG05) :
/// depuis le refactor, l'ÉTAT et le JOURNAL vivent dans DocumentApproval ; cette table ne conserve plus que les
/// données fiscales PROPRES au module Mandats — le BT-1 alloué (<c>allocated_number</c>, MND05/ADR-0025, écrit
/// par <c>PostgresSelfBilledNumberAllocator</c>, hors payload hashé) et l'instant d'entrée en attente
/// (<c>pending_since</c>). Lecture toujours scopée par <c>company_id</c> (CLAUDE.md n°9, INV-MANDATS-1).
/// </summary>
internal static class SelfBilledAcceptanceCompanionReader
{
    private const string SelectSql = """
        SELECT allocated_number, pending_since
        FROM mandats.self_billed_acceptances
        WHERE company_id = @CompanyId AND document_id = @DocumentId
        """;

    public static async Task<SelfBilledAcceptanceCompanion?> LoadAsync(
        IDbConnection connection,
        Guid companyId,
        Guid documentId,
        IDbTransaction? transaction,
        CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(
                SelectSql,
                new { CompanyId = companyId, DocumentId = documentId },
                transaction,
                cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        return new SelfBilledAcceptanceCompanion(
            (string?)row.allocated_number,
            MandatsRowReader.ToDateTimeOffset((object)row.pending_since));
    }
}
