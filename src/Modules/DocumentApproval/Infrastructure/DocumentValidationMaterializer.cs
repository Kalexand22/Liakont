namespace Liakont.Modules.DocumentApproval.Infrastructure;

using System.Data;
using Dapper;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Domain.Entities;
using Liakont.Modules.Signature.Contracts;

/// <summary>
/// Charge une <see cref="DocumentValidation"/> (+ ses slots) depuis la base et la reconstitue en agrégat de
/// domaine. Lecture toujours scopée par <c>company_id</c> (CLAUDE.md n°9).
/// </summary>
internal static class DocumentValidationMaterializer
{
    private const string SelectValidationSql = """
        SELECT company_id, document_id, validation_purpose, attempt, state, proof_level,
               express_acceptance_recorded, deadline_utc, created_at, updated_at
        FROM documentapproval.document_validations
        WHERE company_id = @CompanyId AND document_id = @DocumentId
          AND validation_purpose = @Purpose AND attempt = @Attempt
        """;

    private const string SelectSlotsSql = """
        SELECT signer_id, slot_state, proof_level, proof_id
        FROM documentapproval.document_validation_slots
        WHERE company_id = @CompanyId AND document_id = @DocumentId
          AND validation_purpose = @Purpose AND attempt = @Attempt
        ORDER BY signer_id
        """;

    public static async Task<DocumentValidation?> LoadAsync(
        IDbConnection connection,
        Guid companyId,
        Guid documentId,
        ValidationPurpose purpose,
        int attempt,
        IDbTransaction? transaction,
        CancellationToken ct)
    {
        var parameters = new
        {
            CompanyId = companyId,
            DocumentId = documentId,
            Purpose = (int)purpose,
            Attempt = attempt,
        };

        var row = await connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(SelectValidationSql, parameters, transaction, cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        var slotRows = await connection.QueryAsync(
            new CommandDefinition(SelectSlotsSql, parameters, transaction, cancellationToken: ct));

        var slots = slotRows.Select(MapSlot).ToList();
        return Map(row, slots);
    }

    private static DocumentValidation Map(dynamic row, IEnumerable<ApprovalSlot> slots)
    {
        return DocumentValidation.Reconstitute(
            (Guid)row.company_id,
            (Guid)row.document_id,
            (ValidationPurpose)(int)row.validation_purpose,
            (int)row.attempt,
            (ValidationState)(int)row.state,
            (SignatureLevel)(int)row.proof_level,
            (bool)row.express_acceptance_recorded,
            DocumentApprovalRowReader.ToNullableDateTimeOffset((object?)row.deadline_utc),
            DocumentApprovalRowReader.ToDateTimeOffset((object)row.created_at),
            DocumentApprovalRowReader.ToNullableDateTimeOffset((object?)row.updated_at),
            slots);
    }

    private static ApprovalSlot MapSlot(dynamic row)
    {
        return ApprovalSlot.Reconstitute(
            (string)row.signer_id,
            (ApprovalSlotState)(int)row.slot_state,
            (SignatureLevel)(int)row.proof_level,
            (string?)row.proof_id);
    }
}
