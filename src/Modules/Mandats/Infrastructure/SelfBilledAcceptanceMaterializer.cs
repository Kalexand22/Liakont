namespace Liakont.Modules.Mandats.Infrastructure;

using System.Data;
using Dapper;
using Liakont.Modules.Mandats.Domain.Entities;

/// <summary>
/// Charge une <see cref="SelfBilledAcceptance"/> depuis la base et la reconstitue en agrégat de domaine.
/// Lecture toujours scopée par <c>company_id</c> (CLAUDE.md n°9, INV-MANDATS-1).
/// </summary>
internal static class SelfBilledAcceptanceMaterializer
{
    private const string SelectSql = """
        SELECT company_id, document_id, state, allocated_number, pending_since, deadline_utc,
               created_at, updated_at
        FROM mandats.self_billed_acceptances
        WHERE company_id = @CompanyId AND document_id = @DocumentId
        """;

    public static async Task<SelfBilledAcceptance?> LoadAsync(
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

        return row is null ? null : Map(row);
    }

    public static SelfBilledAcceptance Map(dynamic row)
    {
        return SelfBilledAcceptance.Reconstitute(
            (Guid)row.company_id,
            (Guid)row.document_id,
            (SelfBilledAcceptanceState)(int)row.state,
            (string?)row.allocated_number,
            MandatsRowReader.ToDateTimeOffset((object)row.pending_since),
            MandatsRowReader.ToNullableDateTimeOffset((object?)row.deadline_utc),
            MandatsRowReader.ToDateTimeOffset((object)row.created_at),
            MandatsRowReader.ToNullableDateTimeOffset((object?)row.updated_at));
    }
}
