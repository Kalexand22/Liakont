namespace Liakont.Host.Signatures;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Host.Security;
using Liakont.Modules.DocumentApproval.Contracts;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Implémentation de <see cref="ISignatureConsoleActions"/>. Réutilise le port générique
/// <see cref="IDocumentApprovalWorkflow"/> (SIG05) sans dupliquer la machine fermée ni le journal append-only :
/// ici on ne fait que (1) vérifier la permission <c>liakont.actions</c> (défense en profondeur — le masquage des
/// boutons côté UI ne suffit pas), (2) résoudre le tenant (<c>company_id</c>) et l'opérateur depuis le contexte
/// d'acteur, (3) appeler le port et (4) traduire les refus métier ATTENDUS (demande déjà en cours, transition
/// impossible) en messages opérateur français (CLAUDE.md n°12). Les exceptions INATTENDUES ne sont pas avalées :
/// elles remontent à la page, qui les trace et affiche un message générique (RunActionAsync). TENANT-SCOPÉ par
/// construction (la connexion EST le tenant, CLAUDE.md n°9/17). Le journal des transitions est écrit append-only
/// PAR le workflow dans la même transaction (INV-APPROVAL-6) — aucune piste d'audit n'est dupliquée ici.
/// </summary>
internal sealed class SignatureConsoleActionsService : ISignatureConsoleActions
{
    private readonly IDocumentApprovalWorkflow _workflow;
    private readonly IActorContextAccessor _actorAccessor;
    private readonly IPermissionService _permissions;

    public SignatureConsoleActionsService(
        IDocumentApprovalWorkflow workflow,
        IActorContextAccessor actorAccessor,
        IPermissionService permissions)
    {
        _workflow = workflow;
        _actorAccessor = actorAccessor;
        _permissions = permissions;
    }

    public async Task<SignatureActionResult> RequestValidationAsync(
        Guid documentId, ValidationPurpose purpose, DateTimeOffset? deadlineUtc, CancellationToken cancellationToken = default)
    {
        if (DenyIfNotAuthorized() is { } denied)
        {
            return denied;
        }

        if (!TryResolveCompanyId(out var companyId, out var tenantFailure))
        {
            return tenantFailure;
        }

        var actor = _actorAccessor.Current;
        try
        {
            await _workflow.RequestValidationAsync(
                companyId, documentId, purpose, deadlineUtc, OperatorId(actor), OperatorName(actor), cancellationToken).ConfigureAwait(false);
            return SignatureActionResult.Ok(
                "Demande de validation déclenchée : le document est désormais en attente de validation.");
        }
        catch (ConflictException)
        {
            // Index partiel d'unicité des non-terminaux (une seule tentative active par document/finalité).
            return SignatureActionResult.Failure(
                "Une demande de validation est déjà en cours pour ce document et cette finalité.");
        }
    }

    public Task<SignatureActionResult> RecordRecordedAsync(
        Guid documentId, ValidationPurpose purpose, CancellationToken cancellationToken = default) =>
        TransitionAsync(
            (workflow, companyId, actor) => workflow.RecordRecordedValidationAsync(
                companyId, documentId, purpose, OperatorId(actor), OperatorName(actor), cancellationToken),
            "Acceptation enregistrée : le document est validé.");

    public Task<SignatureActionResult> ContestAsync(
        Guid documentId, ValidationPurpose purpose, CancellationToken cancellationToken = default) =>
        TransitionAsync(
            (workflow, companyId, actor) => workflow.ContestAsync(
                companyId, documentId, purpose, OperatorId(actor), OperatorName(actor), cancellationToken),
            "Contestation enregistrée : la validation est définitivement contestée.");

    /// <summary>Identité d'audit de l'opérateur (GUID utilisateur, ou <c>null</c> si non authentifié).</summary>
    private static Guid? OperatorId(IActorContext actor) => actor.IsAuthenticated ? actor.UserId : null;

    /// <summary>Nom d'affichage de l'opérateur pour le journal (repli sur l'e-mail, <c>null</c> si non authentifié).</summary>
    private static string? OperatorName(IActorContext actor) => actor.IsAuthenticated ? (actor.DisplayName ?? actor.Email) : null;

    /// <summary>
    /// Mécanique commune aux transitions sur la tentative la plus récente (acceptation enregistrée, contestation) :
    /// garde de permission, résolution du tenant, appel du port, et traduction du refus de la machine fermée
    /// (<see cref="InvalidOperationException"/> : aucune tentative en attente, ou état terminal) en message opérateur.
    /// Toute exception inattendue remonte à la page (qui la trace).
    /// </summary>
    private async Task<SignatureActionResult> TransitionAsync(
        Func<IDocumentApprovalWorkflow, Guid, IActorContext, Task> transition,
        string successMessage)
    {
        if (DenyIfNotAuthorized() is { } denied)
        {
            return denied;
        }

        if (!TryResolveCompanyId(out var companyId, out var tenantFailure))
        {
            return tenantFailure;
        }

        var actor = _actorAccessor.Current;
        try
        {
            await transition(_workflow, companyId, actor).ConfigureAwait(false);
            return SignatureActionResult.Ok(successMessage);
        }
        catch (InvalidOperationException)
        {
            // Machine fermée : aucune tentative en attente, ou déjà finalisée (transition interdite).
            return SignatureActionResult.Failure(
                "Action impossible dans l'état actuel : aucune demande de validation en attente pour ce document et cette finalité (ou la validation est déjà finalisée).");
        }
    }

    /// <summary>
    /// Refuse l'action si l'opérateur ne porte pas <c>liakont.actions</c> (défense en profondeur : la page masque
    /// déjà les boutons, mais le chemin in-process ne doit pas en dépendre). Renvoie <c>null</c> si autorisé.
    /// </summary>
    private SignatureActionResult? DenyIfNotAuthorized() =>
        _permissions.HasPermission(LiakontPermissions.Actions)
            ? null
            : SignatureActionResult.Failure("Action non autorisée : la permission « actions » (liakont.actions) est requise.");

    /// <summary>Résout le tenant (company_id) depuis le contexte d'acteur ; échec propre si non résolu (CLAUDE.md n°9).</summary>
    private bool TryResolveCompanyId(out Guid companyId, out SignatureActionResult failure)
    {
        if (_actorAccessor.Current.CompanyId is { } resolved)
        {
            companyId = resolved;
            failure = SignatureActionResult.Ok(string.Empty);
            return true;
        }

        companyId = Guid.Empty;
        failure = SignatureActionResult.Failure("Tenant non résolu : action de validation impossible.");
        return false;
    }
}
