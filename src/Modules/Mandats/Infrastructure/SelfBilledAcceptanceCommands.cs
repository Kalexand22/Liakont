namespace Liakont.Modules.Mandats.Infrastructure;

using Dapper;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.Mandats.Contracts;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Implémentation du cycle de vie de l'acceptation 389 (SIG05) : délègue l'ÉTAT + le JOURNAL au module générique
/// DocumentApproval (<see cref="IDocumentApprovalWorkflow"/>, purpose <see cref="ValidationPurpose.SelfBilledAcceptance"/>,
/// machine fermée + <c>document_approval_log</c> append-only) et maintient la companion fiscale du module Mandats
/// (<c>mandats.self_billed_acceptances</c> : <c>pending_since</c> + emplacement du BT-1 <c>allocated_number</c>
/// rempli par MND05). Aucune logique fiscale dupliquée ; la machine et le journal sont ceux de DocumentApproval.
/// </summary>
internal sealed class SelfBilledAcceptanceCommands : ISelfBilledAcceptanceCommands
{
    // Crée la companion fiscale (allocated_number rempli plus tard par l'allocateur MND05 ; created_at par défaut).
    // ON CONFLICT DO NOTHING : ré-ouverture idempotente après échec partiel — la genèse de DocumentApproval (ci-dessous)
    // porte la garde d'unicité réelle (index partiel des non-terminaux, ConflictException).
    private const string InsertCompanionSql = """
        INSERT INTO mandats.self_billed_acceptances (company_id, document_id, pending_since)
        VALUES (@CompanyId, @DocumentId, @PendingSince)
        ON CONFLICT (company_id, document_id) DO NOTHING
        """;

    private readonly IDocumentApprovalWorkflow _workflow;
    private readonly IConnectionFactory _connectionFactory;

    public SelfBilledAcceptanceCommands(IDocumentApprovalWorkflow workflow, IConnectionFactory connectionFactory)
    {
        _workflow = workflow;
        _connectionFactory = connectionFactory;
    }

    public async Task OpenPendingAsync(
        Guid companyId,
        Guid documentId,
        DateTimeOffset pendingSince,
        DateTimeOffset? deadlineUtc,
        Guid? operatorId,
        string? operatorName,
        CancellationToken ct = default)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Le tenant (company_id) est obligatoire.", nameof(companyId));
        }

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("Le document concerné (document_id) est obligatoire.", nameof(documentId));
        }

        if (deadlineUtc is not null && deadlineUtc.Value < pendingSince)
        {
            throw new ArgumentException(
                "L'échéance de bascule tacite ne peut pas précéder l'entrée en attente (deadline_utc < pending_since).",
                nameof(deadlineUtc));
        }

        // Companion fiscale D'ABORD (idempotente) : garantit que la ligne existe pour l'allocateur MND05 et pour
        // la lecture de pending_since AVANT que l'état ne devienne visible. La garde d'unicité réelle est portée
        // par la genèse DocumentApproval (ConflictException si une tentative non terminale existe déjà).
        using (var connection = await _connectionFactory.OpenAsync(ct))
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    InsertCompanionSql,
                    new { CompanyId = companyId, DocumentId = documentId, PendingSince = pendingSince },
                    cancellationToken: ct));
        }

        await _workflow.RequestValidationAsync(
            companyId, documentId, ValidationPurpose.SelfBilledAcceptance, deadlineUtc, operatorId, operatorName, ct);
    }

    public Task AcceptExpresslyAsync(
        Guid companyId, Guid documentId, Guid? operatorId, string? operatorName, CancellationToken ct = default)
        => _workflow.RecordRecordedValidationAsync(
            companyId, documentId, ValidationPurpose.SelfBilledAcceptance, operatorId, operatorName, ct);

    public Task ContestAsync(
        Guid companyId, Guid documentId, Guid? operatorId, string? operatorName, CancellationToken ct = default)
        => _workflow.ContestAsync(
            companyId, documentId, ValidationPurpose.SelfBilledAcceptance, operatorId, operatorName, ct);
}
