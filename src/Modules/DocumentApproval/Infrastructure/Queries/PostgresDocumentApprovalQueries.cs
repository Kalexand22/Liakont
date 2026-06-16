namespace Liakont.Modules.DocumentApproval.Infrastructure.Queries;

using System.Data;
using Dapper;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Contracts.DTOs;
using Liakont.Modules.DocumentApproval.Contracts.Queries;
using Liakont.Modules.DocumentApproval.Domain.Entities;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lectures Dapper du workflow de validation (ADR-0028). Toujours scopées par <c>company_id</c> (CLAUDE.md
/// n°9). La projection passe par l'agrégat de domaine (matérialiseur) pour exposer un état dérivé cohérent
/// (terminalité, noms d'états) sans dupliquer la machine.
/// </summary>
internal sealed class PostgresDocumentApprovalQueries : IDocumentApprovalQueries
{
    private const string MaxAttemptSql = """
        SELECT max(attempt)
        FROM documentapproval.document_validations
        WHERE company_id = @CompanyId AND document_id = @DocumentId AND validation_purpose = @Purpose
        """;

    private const string LogSql = """
        SELECT document_id, validation_purpose, attempt, from_state, to_state, signer_id,
               operator_id, operator_name, occurred_at
        FROM documentapproval.document_approval_log
        WHERE company_id = @CompanyId AND document_id = @DocumentId AND validation_purpose = @Purpose
        ORDER BY occurred_at DESC, seq DESC
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresDocumentApprovalQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<DocumentValidationDto?> GetLatestAttempt(
        Guid companyId, Guid documentId, ValidationPurpose purpose, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenAsync(ct);

        var maxAttempt = await connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(
                MaxAttemptSql,
                new { CompanyId = companyId, DocumentId = documentId, Purpose = (int)purpose },
                cancellationToken: ct));

        if (maxAttempt is null)
        {
            return null;
        }

        var aggregate = await DocumentValidationMaterializer.LoadAsync(
            connection, companyId, documentId, purpose, maxAttempt.Value, transaction: null, ct);

        return aggregate is null ? null : MapValidation(aggregate);
    }

    public async Task<IReadOnlyList<DocumentApprovalLogEntryDto>> GetApprovalLog(
        Guid companyId, Guid documentId, ValidationPurpose purpose, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenAsync(ct);

        var rows = await connection.QueryAsync(
            new CommandDefinition(
                LogSql,
                new { CompanyId = companyId, DocumentId = documentId, Purpose = (int)purpose },
                cancellationToken: ct));

        var entries = new List<DocumentApprovalLogEntryDto>();
        foreach (var row in rows)
        {
            int? fromState = row.from_state is null ? null : (int)row.from_state;
            entries.Add(new DocumentApprovalLogEntryDto
            {
                DocumentId = (Guid)row.document_id,
                Purpose = (ValidationPurpose)(int)row.validation_purpose,
                Attempt = (int)row.attempt,
                FromState = fromState is null ? null : ((ValidationState)fromState.Value).ToString(),
                ToState = ((ValidationState)(int)row.to_state).ToString(),
                SignerId = (string?)row.signer_id,
                OperatorId = (Guid?)row.operator_id,
                OperatorName = (string?)row.operator_name,
                OccurredAt = DocumentApprovalRowReader.ToDateTimeOffset((object)row.occurred_at),
            });
        }

        return entries;
    }

    private static DocumentValidationDto MapValidation(DocumentValidation v)
        => new()
        {
            DocumentId = v.DocumentId,
            Purpose = v.Purpose,
            Attempt = v.Attempt,
            State = v.State.ToString(),
            ProofLevel = v.ProofLevel.ToString(),
            ExpressAcceptanceRecorded = v.ExpressAcceptanceRecorded,
            DeadlineUtc = v.DeadlineUtc,
            IsTerminal = v.IsTerminal,
            Slots = v.Slots
                .Select(s => new ApprovalSlotDto
                {
                    SignerId = s.SignerId,
                    State = s.State.ToString(),
                    ProofLevel = s.ProofLevel.ToString(),
                    ProofId = s.ProofId,
                })
                .ToList(),
        };
}
