namespace Liakont.Host.Documents;

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Host.Security;
using Liakont.Modules.Documents.Contracts;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Pipeline.Contracts;
using Stratum.Common.Abstractions.Audit;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Implémentation de <see cref="IDocumentControlActions"/>. Réutilise <b>à l'identique</b> l'orchestration
/// des endpoints API02b (<c>DocumentActionsEndpointMapping</c> : <c>POST /documents/{id}/verdict</c> et
/// <c>/recheck</c>) — mêmes gardes d'état, mêmes ports (<see cref="IDocumentLifecycle"/> /
/// <see cref="IDocumentRecheckService"/>), mêmes codes d'audit et même identité d'opérateur, via la SOURCE
/// UNIQUE partagée <see cref="DocumentActionContract"/> (aucune divergence silencieuse de piste d'audit
/// entre le canal HTTP et le canal console). La console appelle ce service in-process depuis son circuit
/// serveur (le cookie OIDC n'est pas disponible pour boucler sur l'endpoint HTTP, précédent WEB05). Aucune
/// logique fiscale ni machine à états n'est dupliquée : les transitions et la re-validation restent dans les
/// modules ; ici on ne fait que vérifier l'autorisation, valider l'état, appeler le port et journaliser
/// l'action de l'opérateur. TENANT-SCOPÉ par construction (la connexion EST le tenant).
/// </summary>
internal sealed class DocumentControlActionsService : IDocumentControlActions
{
    private readonly IDocumentQueries _documents;
    private readonly IDocumentLifecycle _lifecycle;
    private readonly IDocumentRecheckService _recheck;
    private readonly IActorContextAccessor _actorAccessor;
    private readonly IActivityLogger _activityLogger;
    private readonly IPermissionService _permissions;

    public DocumentControlActionsService(
        IDocumentQueries documents,
        IDocumentLifecycle lifecycle,
        IDocumentRecheckService recheck,
        IActorContextAccessor actorAccessor,
        IActivityLogger activityLogger,
        IPermissionService permissions)
    {
        _documents = documents;
        _lifecycle = lifecycle;
        _recheck = recheck;
        _actorAccessor = actorAccessor;
        _activityLogger = activityLogger;
        _permissions = permissions;
    }

    public async Task<DocumentControlActionResult> SubmitVerdictAsync(
        Guid documentId, ConsoleVerdict verdict, CancellationToken cancellationToken = default)
    {
        if (DenyIfNotAuthorized() is { } denied)
        {
            return denied;
        }

        var document = await _documents.GetByIdAsync(documentId, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return DocumentControlActionResult.Failure("Document introuvable dans ce tenant.");
        }

        // Garde d'état identique à l'endpoint /verdict (409 si non Blocked) : le verdict du garde-fou
        // B2B/B2C ne s'applique qu'à un document bloqué. Bloquer le refus ici évite l'exception du domaine.
        if (!string.Equals(document.State, DocumentActionContract.BlockedState, StringComparison.Ordinal))
        {
            return DocumentControlActionResult.Failure(string.Create(
                CultureInfo.CurrentCulture,
                $"Le verdict du garde-fou B2B/B2C ne s'applique qu'à un document bloqué (document {document.DocumentNumber}, état actuel : {document.State})."));
        }

        var actor = _actorAccessor.Current;
        var operatorId = ActorId(actor);

        if (verdict == ConsoleVerdict.ConfirmIndividualB2c)
        {
            // Enregistre la décision B2C (persistée + journalisée) SANS changer l'état (la re-vérification débloque).
            await _lifecycle.ConfirmBuyerAsIndividualAsync(documentId, operatorId, cancellationToken).ConfigureAwait(false);
            await _activityLogger.LogActivityAsync(
                DocumentActionContract.DocumentEntityType,
                documentId.ToString(),
                DocumentActionContract.VerdictConfirmB2cActivity,
                string.Create(CultureInfo.InvariantCulture, $"Garde-fou B2B/B2C : acheteur confirmé « particulier » (B2C) par l'opérateur pour le document {document.DocumentNumber} (F08 §A.4). Re-vérifier pour débloquer."),
                operatorId,
                metadata: new { document.DocumentNumber, Verdict = DocumentActionContract.VerdictConfirmB2c },
                companyId: actor.CompanyId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Le document reste Blocked : on retourne son état courant (inchangé par le verdict).
            return DocumentControlActionResult.Ok(
                string.Create(CultureInfo.CurrentCulture, $"Acheteur confirmé « particulier » (B2C) pour le document {document.DocumentNumber}. Re-vérifiez maintenant pour débloquer l'envoi."),
                document.State);
        }

        // handle_manually : Blocked → ManuallyHandled (terminal), via la résolution terminale PARTAGÉE (API02c,
        // ResolveManuallyAsync) — une seule mécanique de traitement manuel, motif dérivé du garde-fou (B2B hors
        // passerelle). Le résultat (pas d'exception) mappe proprement un refus concurrent en message opérateur.
        var manualOutcome = await _lifecycle.ResolveManuallyAsync(documentId, DocumentActionContract.ManualB2bReason, operatorId, cancellationToken).ConfigureAwait(false);
        if (manualOutcome is not DocumentResolutionOutcome.Succeeded)
        {
            return DocumentControlActionResult.Failure(ResolutionFailureMessage(manualOutcome, document.DocumentNumber));
        }

        await _activityLogger.LogActivityAsync(
            DocumentActionContract.DocumentEntityType,
            documentId.ToString(),
            DocumentActionContract.VerdictHandleManuallyActivity,
            string.Create(CultureInfo.InvariantCulture, $"Garde-fou B2B/B2C : document {document.DocumentNumber} traité manuellement hors passerelle (B2B) par l'opérateur (F08 §A.4)."),
            operatorId,
            metadata: new { document.DocumentNumber, Verdict = DocumentActionContract.VerdictHandleManually },
            companyId: actor.CompanyId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return DocumentControlActionResult.Ok(
            string.Create(CultureInfo.CurrentCulture, $"Document {document.DocumentNumber} traité manuellement hors passerelle (B2B)."),
            DocumentActionContract.ManuallyHandledState);
    }

    public async Task<DocumentControlActionResult> RecheckAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        if (DenyIfNotAuthorized() is { } denied)
        {
            return denied;
        }

        var actor = _actorAccessor.Current;
        var operatorId = ActorId(actor);
        var result = await _recheck.RecheckAsync(documentId, operatorId, cancellationToken).ConfigureAwait(false);

        switch (result.Outcome)
        {
            case DocumentRecheckOutcome.NotFound:
                return DocumentControlActionResult.Failure("Document introuvable dans ce tenant.");

            case DocumentRecheckOutcome.NotBlocked:
                return DocumentControlActionResult.Failure(string.Create(
                    CultureInfo.CurrentCulture,
                    $"La re-vérification ne s'applique qu'à un document bloqué (état actuel : {result.State})."));

            case DocumentRecheckOutcome.ContentUnavailable:
                return DocumentControlActionResult.Failure(
                    "Le contenu du document n'est pas disponible pour la re-vérification (pas encore stagé, ou altéré/illisible). Action : relancez l'extraction du document depuis le logiciel source, puis réessayez.");

            default:
                // ReadyToSend ou StillBlocked : la re-vérification a tourné — le geste opérateur et son résultat
                // sont déjà tracés dans la piste d'audit append-only par le service (item FIX02). On journalise
                // aussi l'activité (comme l'endpoint) et on rend le résultat ; le motif de re-blocage éventuel est
                // renvoyé pour affichage immédiat.
                await _activityLogger.LogActivityAsync(
                    DocumentActionContract.DocumentEntityType,
                    documentId.ToString(),
                    DocumentActionContract.RecheckActivity,
                    string.Create(CultureInfo.InvariantCulture, $"Re-vérification déclenchée par l'opérateur — résultat : « {result.State} »."),
                    operatorId,
                    metadata: new { State = result.State, Outcome = result.Outcome.ToString() },
                    companyId: actor.CompanyId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (result.Outcome == DocumentRecheckOutcome.ReadyToSend)
                {
                    return DocumentControlActionResult.Ok(
                        "Re-vérification réussie : le document est maintenant prêt à l'envoi.",
                        result.State);
                }

                // StillBlocked : le document reste bloqué avec un (nouveau) motif — succès de l'opération, mais
                // l'opérateur doit voir pourquoi il reste bloqué (CLAUDE.md n°12). La page recharge les contrôles.
                var reason = string.IsNullOrWhiteSpace(result.BlockingReason)
                    ? "Consultez l'onglet Contrôles pour le détail."
                    : result.BlockingReason!;
                return DocumentControlActionResult.Ok(
                    string.Create(CultureInfo.CurrentCulture, $"Re-vérification effectuée : le document reste bloqué. {reason}"),
                    result.State);
        }
    }

    /// <summary>Identité d'audit de l'opérateur (GUID utilisateur ; « system » si non authentifié) — identique aux endpoints.</summary>
    private static string ActorId(IActorContext actor) =>
        actor.IsAuthenticated ? actor.UserId.ToString() : "system";

    /// <summary>Message opérateur pour un refus de résolution manuelle (B2B), citant le numéro de document (CLAUDE.md n°12).</summary>
    private static string ResolutionFailureMessage(DocumentResolutionOutcome outcome, string documentNumber) => outcome switch
    {
        DocumentResolutionOutcome.DocumentNotFound => "Document introuvable dans ce tenant.",
        DocumentResolutionOutcome.InvalidState => string.Create(
            CultureInfo.CurrentCulture,
            $"Le document {documentNumber} n'est plus dans un état permettant le traitement manuel (il a peut-être déjà été résolu)."),
        _ => string.Create(
            CultureInfo.CurrentCulture,
            $"Le traitement manuel du document {documentNumber} n'a pas pu être appliqué."),
    };

    /// <summary>
    /// Refuse l'action si l'opérateur ne porte pas <c>liakont.actions</c> (défense en profondeur : l'endpoint
    /// HTTP porte la même garde via <c>RequireAuthorization</c> ; le chemin in-process de la console ne doit pas
    /// dépendre du seul masquage des boutons côté UI). Renvoie <c>null</c> quand l'action est autorisée.
    /// </summary>
    private DocumentControlActionResult? DenyIfNotAuthorized() =>
        _permissions.HasPermission(LiakontPermissions.Actions)
            ? null
            : DocumentControlActionResult.Failure("Action non autorisée : la permission « actions » (liakont.actions) est requise.");
}
