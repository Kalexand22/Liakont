namespace Liakont.Modules.DocumentApproval.Infrastructure;

using Liakont.Modules.DocumentApproval.Application;
using Liakont.Modules.DocumentApproval.Domain.Entities;

/// <summary>
/// Construit les entrées du journal append-only des transitions de validation (INV-APPROVAL-6) à partir de
/// l'agrégat (après transition). La persistance atomique (transition + entrée dans la même transaction) est
/// assurée par <c>IDocumentValidationUnitOfWork</c>.
/// </summary>
internal static class DocumentApprovalLogFactory
{
    /// <summary>Entrée de genèse : création de la tentative (état initial), <c>from</c> = <c>null</c>.</summary>
    public static DocumentApprovalLogEntry ForCreation(
        DocumentValidation validation, Guid? operatorId, string? operatorName)
        => new()
        {
            CompanyId = validation.CompanyId,
            DocumentId = validation.DocumentId,
            Purpose = validation.Purpose,
            Attempt = validation.Attempt,
            FromState = null,
            ToState = validation.State,
            SignerId = null,
            OperatorId = operatorId,
            OperatorName = operatorName,
        };

    /// <summary>
    /// Entrée d'une transition d'état : <paramref name="fromState"/> → <c>validation.State</c>.
    /// <paramref name="signerId"/> non null = transition déclenchée par un slot (N-parties).
    /// <paramref name="operatorId"/> <c>null</c> = transition SYSTÈME (bascule tacite / timeout par job).
    /// </summary>
    public static DocumentApprovalLogEntry ForTransition(
        DocumentValidation validation,
        ValidationState fromState,
        Guid? operatorId,
        string? operatorName,
        string? signerId = null)
        => new()
        {
            CompanyId = validation.CompanyId,
            DocumentId = validation.DocumentId,
            Purpose = validation.Purpose,
            Attempt = validation.Attempt,
            FromState = fromState,
            ToState = validation.State,
            SignerId = signerId,
            OperatorId = operatorId,
            OperatorName = operatorName,
        };
}
