namespace Liakont.Modules.DocumentApproval.Infrastructure;

using Dapper;
using Liakont.Modules.DocumentApproval.Application;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Domain;
using Liakont.Modules.DocumentApproval.Domain.Entities;
using Npgsql;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Unité de travail Dapper du workflow de validation (ADR-0028). Persiste l'agrégat
/// <see cref="DocumentValidation"/> (état mutable + slots) de façon atomique, scopée par <c>company_id</c>
/// (CLAUDE.md n°9). CHAQUE transition (création incluse) écrit l'agrégat ET son entrée de journal append-only
/// (<c>document_approval_log</c>) dans la MÊME transaction (INV-APPROVAL-6 : « pas de transition sans ligne de
/// journal »). Le journal est immuable (double trigger base, jamais un chemin de code).
/// </summary>
internal sealed class PostgresDocumentValidationUnitOfWork : IDocumentValidationUnitOfWork
{
    private const string InsertValidationSql = """
        INSERT INTO documentapproval.document_validations
            (company_id, document_id, validation_purpose, attempt, state, proof_level,
             express_acceptance_recorded, deadline_utc, created_at, updated_at)
        VALUES
            (@CompanyId, @DocumentId, @Purpose, @Attempt, @State, @ProofLevel,
             @ExpressAcceptanceRecorded, @DeadlineUtc, @CreatedAt, @UpdatedAt)
        """;

    private const string UpdateValidationSql = """
        UPDATE documentapproval.document_validations
        SET state = @State,
            proof_level = @ProofLevel,
            express_acceptance_recorded = @ExpressAcceptanceRecorded,
            updated_at = @UpdatedAt
        WHERE company_id = @CompanyId AND document_id = @DocumentId
          AND validation_purpose = @Purpose AND attempt = @Attempt
        """;

    private const string LockValidationSql = """
        SELECT 1
        FROM documentapproval.document_validations
        WHERE company_id = @CompanyId AND document_id = @DocumentId
          AND validation_purpose = @Purpose AND attempt = @Attempt
        FOR UPDATE
        """;

    private const string LockLatestAttemptSql = """
        SELECT attempt
        FROM documentapproval.document_validations
        WHERE company_id = @CompanyId AND document_id = @DocumentId AND validation_purpose = @Purpose
        ORDER BY attempt DESC
        LIMIT 1
        FOR UPDATE
        """;

    private const string DeleteSlotsSql = """
        DELETE FROM documentapproval.document_validation_slots
        WHERE company_id = @CompanyId AND document_id = @DocumentId
          AND validation_purpose = @Purpose AND attempt = @Attempt
        """;

    private const string InsertSlotSql = """
        INSERT INTO documentapproval.document_validation_slots
            (company_id, document_id, validation_purpose, attempt, signer_id, slot_state, proof_level, proof_id, updated_at)
        VALUES
            (@CompanyId, @DocumentId, @Purpose, @Attempt, @SignerId, @SlotState, @ProofLevel, @ProofId, @UpdatedAt)
        """;

    private const string InsertLogSql = """
        INSERT INTO documentapproval.document_approval_log
            (company_id, document_id, validation_purpose, attempt, from_state, to_state, signer_id,
             operator_id, operator_name)
        VALUES
            (@CompanyId, @DocumentId, @Purpose, @Attempt, @FromState, @ToState, @SignerId,
             @OperatorId, @OperatorName)
        """;

    private readonly TransactionScope _txn;

    private PostgresDocumentValidationUnitOfWork(TransactionScope txn)
    {
        _txn = txn;
    }

    public static async Task<PostgresDocumentValidationUnitOfWork> BeginAsync(
        IConnectionFactory connectionFactory,
        CancellationToken ct = default)
    {
        var txn = await TransactionScope.BeginAsync(connectionFactory, ct);
        return new PostgresDocumentValidationUnitOfWork(txn);
    }

    public async Task InsertAsync(
        DocumentValidation validation, DocumentApprovalLogEntry logEntry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(logEntry);

        await InsertCoreAsync(validation, logEntry, ct);
    }

    public async Task<DocumentValidation?> GetForUpdateAsync(
        Guid companyId, Guid documentId, ValidationPurpose purpose, int attempt, CancellationToken ct = default)
    {
        var locked = await _txn.Connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(
                LockValidationSql,
                new { CompanyId = companyId, DocumentId = documentId, Purpose = (int)purpose, Attempt = attempt },
                _txn.Transaction,
                cancellationToken: ct));

        if (locked is null)
        {
            return null;
        }

        return await DocumentValidationMaterializer.LoadAsync(
            _txn.Connection, companyId, documentId, purpose, attempt, _txn.Transaction, ct);
    }

    public async Task<DocumentValidation?> GetLatestForUpdateAsync(
        Guid companyId, Guid documentId, ValidationPurpose purpose, CancellationToken ct = default)
    {
        var latestAttempt = await LockLatestAttemptAsync(companyId, documentId, purpose, ct);
        if (latestAttempt is null)
        {
            return null;
        }

        return await DocumentValidationMaterializer.LoadAsync(
            _txn.Connection, companyId, documentId, purpose, latestAttempt.Value, _txn.Transaction, ct);
    }

    public async Task SaveTransitionAsync(
        DocumentValidation validation, DocumentApprovalLogEntry logEntry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(logEntry);

        var affected = await _txn.Connection.ExecuteAsync(
            new CommandDefinition(UpdateValidationSql, ValidationParameters(validation), _txn.Transaction, cancellationToken: ct));

        if (affected != 1)
        {
            throw new InvalidOperationException(
                "La tentative de validation ciblée par la transition est introuvable pour ce tenant.");
        }

        // Slots : réécriture complète depuis l'agrégat (DELETE + INSERT) — l'agrégat est la source de vérité de
        // l'état des slots. Dans la MÊME transaction que la mutation et le journal.
        await RewriteSlotsAsync(validation, ct);

        // Journal APPEND-ONLY, dans la même transaction → atomicité (INV-APPROVAL-6) : un échec ici annule
        // aussi la transition (et inversement).
        await InsertLogAsync(logEntry, ct);
    }

    public async Task<DocumentValidation> CreateNextAttemptAsync(
        Guid companyId,
        Guid documentId,
        ValidationPurpose purpose,
        DateTimeOffset? deadlineUtc,
        IEnumerable<string>? signerIds,
        Guid? operatorId,
        string? operatorName,
        CancellationToken ct = default)
    {
        var policy = ValidationPurposePolicy.For(purpose);
        if (!policy.AllowsRetry)
        {
            throw new InvalidOperationException(
                $"Le purpose « {purpose} » est EXCLU du ré-essai (ADR-0028 §6) : correction = document " +
                "compensatoire + nouveau document_id, jamais une nouvelle tentative.");
        }

        // Garde anti-race (INV-APPROVAL-5) : verrouille la tentative N (FOR UPDATE) et n'autorise la création
        // de N+1 QUE si N est un échec terminal (Expired/Rejected), dans la MÊME transaction. Sinon un succès
        // concurrent de N (webhook) serait masqué par un N+1 non terminal qui refermerait le gate à tort.
        var latestAttempt = await LockLatestAttemptAsync(companyId, documentId, purpose, ct);
        if (latestAttempt is null)
        {
            throw new InvalidOperationException(
                "Aucune tentative existante pour ce document/purpose : utiliser InsertAsync pour la première tentative.");
        }

        var latest = await DocumentValidationMaterializer.LoadAsync(
            _txn.Connection, companyId, documentId, purpose, latestAttempt.Value, _txn.Transaction, ct);

        if (latest!.State is not (ValidationState.Expired or ValidationState.Rejected))
        {
            throw new InvalidOperationException(
                $"Ré-essai refusé : la dernière tentative (attempt {latest.Attempt}, état « {latest.State} ») " +
                "n'est pas un échec terminal (Expired/Rejected). Une nouvelle tentative ne peut masquer une " +
                "tentative en cours ou réussie (garde anti-race, INV-APPROVAL-5).");
        }

        var next = DocumentValidation.Create(companyId, documentId, purpose, deadlineUtc, latest.Attempt + 1, signerIds);
        var genesis = DocumentApprovalLogFactory.ForCreation(next, operatorId, operatorName);
        await InsertCoreAsync(next, genesis, ct);
        return next;
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _txn.CommitAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _txn.DisposeAsync();
    }

    private static object ValidationParameters(DocumentValidation validation)
        => new
        {
            validation.CompanyId,
            validation.DocumentId,
            Purpose = (int)validation.Purpose,
            validation.Attempt,
            State = (int)validation.State,
            ProofLevel = (int)validation.ProofLevel,
            validation.ExpressAcceptanceRecorded,
            validation.DeadlineUtc,
            validation.CreatedAt,
            validation.UpdatedAt,
        };

    private async Task<int?> LockLatestAttemptAsync(
        Guid companyId, Guid documentId, ValidationPurpose purpose, CancellationToken ct)
        => await _txn.Connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(
                LockLatestAttemptSql,
                new { CompanyId = companyId, DocumentId = documentId, Purpose = (int)purpose },
                _txn.Transaction,
                cancellationToken: ct));

    private async Task InsertCoreAsync(
        DocumentValidation validation, DocumentApprovalLogEntry logEntry, CancellationToken ct)
    {
        try
        {
            await _txn.Connection.ExecuteAsync(
                new CommandDefinition(InsertValidationSql, ValidationParameters(validation), _txn.Transaction, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ConflictException(
                "Une tentative de validation non terminale existe déjà pour ce document/purpose dans ce tenant (au plus une tentative active — INV-APPROVAL-5).", ex);
        }

        await InsertSlotsAsync(validation, ct);
        await InsertLogAsync(logEntry, ct);
    }

    private async Task RewriteSlotsAsync(DocumentValidation validation, CancellationToken ct)
    {
        await _txn.Connection.ExecuteAsync(
            new CommandDefinition(
                DeleteSlotsSql,
                new
                {
                    validation.CompanyId,
                    validation.DocumentId,
                    Purpose = (int)validation.Purpose,
                    validation.Attempt,
                },
                _txn.Transaction,
                cancellationToken: ct));

        await InsertSlotsAsync(validation, ct);
    }

    private async Task InsertSlotsAsync(DocumentValidation validation, CancellationToken ct)
    {
        foreach (var slot in validation.Slots)
        {
            await _txn.Connection.ExecuteAsync(
                new CommandDefinition(
                    InsertSlotSql,
                    new
                    {
                        validation.CompanyId,
                        validation.DocumentId,
                        Purpose = (int)validation.Purpose,
                        validation.Attempt,
                        slot.SignerId,
                        SlotState = (int)slot.State,
                        ProofLevel = (int)slot.ProofLevel,
                        slot.ProofId,
                        validation.UpdatedAt,
                    },
                    _txn.Transaction,
                    cancellationToken: ct));
        }
    }

    private Task<int> InsertLogAsync(DocumentApprovalLogEntry entry, CancellationToken ct)
        => _txn.Connection.ExecuteAsync(
            new CommandDefinition(
                InsertLogSql,
                new
                {
                    entry.CompanyId,
                    entry.DocumentId,
                    Purpose = (int)entry.Purpose,
                    entry.Attempt,
                    FromState = entry.FromState is null ? (int?)null : (int)entry.FromState.Value,
                    ToState = (int)entry.ToState,
                    entry.SignerId,
                    entry.OperatorId,
                    entry.OperatorName,
                },
                _txn.Transaction,
                cancellationToken: ct));
}
