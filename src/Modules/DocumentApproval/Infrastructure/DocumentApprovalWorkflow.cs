namespace Liakont.Modules.DocumentApproval.Infrastructure;

using Liakont.Modules.DocumentApproval.Application;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Domain.Entities;
using Liakont.Modules.Signature.Contracts;

/// <summary>
/// Implémentation du port de commande générique <see cref="IDocumentApprovalWorkflow"/> (ADR-0028) : pilote le
/// cycle de vie d'une validation par-dessus <see cref="IDocumentValidationUnitOfWork"/>, en gardant l'atomicité
/// transition + journal (INV-APPROVAL-6) et le verrou de transition (<c>FOR UPDATE</c>). Aucun module exposeur
/// (Mandats, etc.) ne touche la persistance ni la machine de domaine : il passe par ce port (frontière Contracts).
/// </summary>
internal sealed class DocumentApprovalWorkflow : IDocumentApprovalWorkflow
{
    private readonly IDocumentValidationUnitOfWorkFactory _unitOfWorkFactory;

    public DocumentApprovalWorkflow(IDocumentValidationUnitOfWorkFactory unitOfWorkFactory)
    {
        _unitOfWorkFactory = unitOfWorkFactory;
    }

    public async Task RequestValidationAsync(
        Guid companyId,
        Guid documentId,
        ValidationPurpose purpose,
        DateTimeOffset? deadlineUtc,
        Guid? operatorId,
        string? operatorName,
        CancellationToken ct = default)
    {
        var validation = DocumentValidation.Create(companyId, documentId, purpose, deadlineUtc);
        var genesis = DocumentApprovalLogFactory.ForCreation(validation, operatorId, operatorName);

        await using var uow = await _unitOfWorkFactory.BeginAsync(ct);
        await uow.InsertAsync(validation, genesis, ct);
        await uow.CommitAsync(ct);
    }

    public Task RecordRecordedValidationAsync(
        Guid companyId,
        Guid documentId,
        ValidationPurpose purpose,
        Guid? operatorId,
        string? operatorName,
        CancellationToken ct = default)
        => TransitionLatestAsync(
            companyId,
            documentId,
            purpose,
            operatorId,
            operatorName,
            v => v.Validate(SignatureLevel.Recorded),
            ct);

    public Task ContestAsync(
        Guid companyId,
        Guid documentId,
        ValidationPurpose purpose,
        Guid? operatorId,
        string? operatorName,
        CancellationToken ct = default)
        => TransitionLatestAsync(
            companyId,
            documentId,
            purpose,
            operatorId,
            operatorName,
            v => v.Contest(),
            ct);

    public async Task<bool> RecordTacitValidationIfDueAsync(
        Guid companyId,
        Guid documentId,
        ValidationPurpose purpose,
        DateTimeOffset nowUtc,
        string? operatorName,
        CancellationToken ct = default)
    {
        await using var uow = await _unitOfWorkFactory.BeginAsync(ct);
        var validation = await uow.GetLatestForUpdateAsync(companyId, documentId, purpose, ct);

        // Re-vérification SOUS VERROU (anti-TOCTOU) : l'état a pu changer entre l'énumération (lecteur) et le
        // verrou (acceptation expresse / contestation concurrente, ou échéance non échue). Si plus éligible, on
        // ne transite pas — l'abandon de la transaction (DisposeAsync sans Commit) ne laisse aucun effet.
        if (validation is null
            || validation.State != ValidationState.PendingValidation
            || validation.DeadlineUtc is not { } deadline
            || deadline > nowUtc)
        {
            return false;
        }

        var fromState = validation.State;
        validation.MarkTacitlyValidated();
        var entry = DocumentApprovalLogFactory.ForTransition(
            validation, fromState, operatorId: null, operatorName: operatorName);

        await uow.SaveTransitionAsync(validation, entry, ct);
        await uow.CommitAsync(ct);
        return true;
    }

    private async Task TransitionLatestAsync(
        Guid companyId,
        Guid documentId,
        ValidationPurpose purpose,
        Guid? operatorId,
        string? operatorName,
        Action<DocumentValidation> transition,
        CancellationToken ct)
    {
        await using var uow = await _unitOfWorkFactory.BeginAsync(ct);
        var validation = await uow.GetLatestForUpdateAsync(companyId, documentId, purpose, ct);
        if (validation is null)
        {
            throw new InvalidOperationException(
                $"Aucune validation enregistrée pour le document {documentId} (purpose « {purpose} ») de ce " +
                "tenant : la transition demandée suppose une tentative existante (genèse via RequestValidationAsync).");
        }

        var fromState = validation.State;
        transition(validation);
        var entry = DocumentApprovalLogFactory.ForTransition(validation, fromState, operatorId, operatorName);

        await uow.SaveTransitionAsync(validation, entry, ct);
        await uow.CommitAsync(ct);
    }
}
