namespace Liakont.Modules.Documents.Web;

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Jobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Audit;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Endpoints d'ACTION du module Documents pour la console (API02a), montés sous <c>/api/v1/documents</c>
/// par le Host. Scaffold partagé : API02b (verdict / re-vérification) et API02c (résolution terminale)
/// ajoutent leurs endpoints ICI, sur la même surface (permission <c>liakont.actions</c>).
/// <para>
/// MODÈLE D'ENVOI (rien d'inventé — ADR-0016) : l'envoi est un job batch PAR TENANT (<c>SendTenantJob</c>,
/// PIP01c). La console ne fait PAS d'envoi synchrone et n'ouvre PAS un second chemin d'envoi (qui dédoublerait
/// la logique fiscale — interdit) : elle <b>publie</b> le déclencheur MONO-TENANT
/// <see cref="SendTenantTrigger"/> sur la queue SYSTÈME (celle que le <c>JobWorker</c> consomme réellement),
/// avec le tenant de l'opérateur dans la charge utile. Le handler système rétablit ce SEUL tenant via
/// <c>ITenantScopeFactory.Create</c> et exécute le SEND — JAMAIS un fan-out tous-tenants (réservé au cron).
/// L'isolation tenant d'une action d'opérateur (CLAUDE.md n°9, blueprint §7) est ainsi garantie par
/// construction (INV-API02a-1/-2/-4). L'envoi proprement dit (lecture du pivot stagé, anti-doublon, archive
/// WORM, transitions) reste dans le pipeline.
/// </para>
/// <para>
/// TENANT-SCOPÉ par construction : les lectures (<see cref="IDocumentQueries"/>) s'exécutent sur la base DU
/// tenant courant (la connexion EST le tenant — blueprint §7, CLAUDE.md n°9/17). Chaque action est
/// journalisée avec l'identité de l'opérateur (<see cref="IActivityLogger"/>, module Audit).
/// </para>
/// </summary>
public static class DocumentActionsEndpointMapping
{
    /// <summary>
    /// Permission d'action opérateur (canonique : <c>LiakontPermissions.Actions</c> dans le Host, cataloguée
    /// par Identity). Référencée en chaîne car un projet de module ne référence pas le Host (frontière).
    /// </summary>
    private const string ActionsPermission = "liakont.actions";

    /// <summary>État d'un document prêt à l'envoi (DocumentState, Domain) : seul état envoyable. SOURCE UNIQUE partagée
    /// avec le service in-process de la console (DocumentSendActionsService, WEB05) via DocumentActionContract.</summary>
    private const string ReadyToSendState = DocumentActionContract.ReadyToSendState;

    // Verdict / re-vérification : identifiants STABLES partagés avec le service in-process de la console
    // (DocumentControlActionsService, WEB03b) via DocumentActionContract — SOURCE UNIQUE pour que les deux
    // canaux (HTTP / console) journalisent le MÊME geste opérateur à l'identique (CLAUDE.md n°4).
    private const string BlockedState = DocumentActionContract.BlockedState;
    private const string VerdictConfirmB2c = DocumentActionContract.VerdictConfirmB2c;
    private const string VerdictHandleManually = DocumentActionContract.VerdictHandleManually;
    private const string ManuallyHandledState = DocumentActionContract.ManuallyHandledState;
    private const string ManualB2bReason = DocumentActionContract.ManualB2bReason;
    private const string DocumentEntityType = DocumentActionContract.DocumentEntityType;

    /// <summary>Taille de page des lectures par état (file bornée — même surface que le pipeline, TRK01).</summary>
    private const int PageSize = 100;

    public static IEndpointRouteBuilder MapDocumentActionsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/documents");

        // POST /api/v1/documents/{id}/send — déclenche l'envoi du document (qui doit être ReadyToSend).
        // L'envoi réel est exécuté par le pipeline (le SEND du tenant émet les ReadyToSend, dont celui-ci) ;
        // l'endpoint valide l'état, publie le déclencheur tenant-scopé et journalise l'action de l'opérateur.
        // 404 hors tenant (la lecture est tenant-scopée), 409 si le document n'est pas ReadyToSend.
        group.MapPost("/{id:guid}/send", async (
            Guid id,
            IDocumentQueries queries,
            IServiceScopeFactory scopeFactory,
            IActorContextAccessor actorAccessor,
            IActivityLogger activityLogger,
            CancellationToken ct) =>
        {
            var document = await queries.GetByIdAsync(id, ct);
            if (document is null)
            {
                return Results.NotFound();
            }

            if (!string.Equals(document.State, ReadyToSendState, StringComparison.Ordinal))
            {
                return Results.Conflict(new ActionProblem(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Le document {document.DocumentNumber} n'est pas prêt à l'envoi (état actuel : {document.State}). Action : ne sont envoyables que les documents à l'état « ReadyToSend ».")));
            }

            var actor = actorAccessor.Current;
            if (string.IsNullOrWhiteSpace(actor.TenantId))
            {
                return Results.BadRequest(new ActionProblem("Tenant non résolu : action d'envoi impossible."));
            }

            var jobId = await PublishTenantSendAsync(scopeFactory, actor.TenantId, actor.CompanyId, ct);

            // Fidélité de la piste d'audit (produit de conformité) : l'action est INDEXÉE sur le document depuis
            // lequel l'opérateur a agi (entityId=id), mais le message dit EXPLICITEMENT son périmètre réel — le
            // SEND du tenant émet TOUS les ReadyToSend (ce document inclus), pas seulement celui-ci (ADR-0016 :
            // pas de chemin d'envoi mono-document ; SendTenantJob boucle sur l'état, pas sur l'id).
            await activityLogger.LogActivityAsync(
                DocumentEntityType,
                id.ToString(),
                DocumentActionContract.SendTriggeredActivity,
                string.Create(CultureInfo.InvariantCulture, $"Envoi déclenché par l'opérateur depuis le document {document.DocumentNumber} : le traitement d'envoi du tenant émet tous les documents prêts à l'envoi (ce document inclus)."),
                ActorId(actor),
                metadata: new { jobId, document.DocumentNumber },
                companyId: actor.CompanyId,
                cancellationToken: ct);

            return Results.Accepted($"/api/v1/documents/{id}", new SendAcceptedResponse(jobId, id));
        }).RequireAuthorization(ActionsPermission);

        // POST /api/v1/documents/send-all — envoi de tous les ReadyToSend du tenant COURANT, avec confirmation :
        //   ?confirm=false (défaut) → récapitulatif (nombre + montant total) SANS rien exécuter ;
        //   ?confirm=true           → publie le déclencheur d'envoi tenant-scopé et journalise l'action.
        // Aucun fan-out cross-tenant : le récapitulatif et l'envoi ne portent que sur le tenant de l'opérateur.
        group.MapPost("/send-all", async (
            bool? confirm,
            IDocumentQueries queries,
            IServiceScopeFactory scopeFactory,
            IActorContextAccessor actorAccessor,
            IActivityLogger activityLogger,
            CancellationToken ct) =>
        {
            var (count, totalGross) = await SummarizeReadyToSendAsync(queries, ct);

            if (confirm != true)
            {
                // Récapitulatif de confirmation : aucune écriture, aucun job publié.
                return Results.Ok(new SendAllSummaryResponse(ConfirmationRequired: true, count, totalGross, JobId: null));
            }

            var actor = actorAccessor.Current;
            if (string.IsNullOrWhiteSpace(actor.TenantId))
            {
                return Results.BadRequest(new ActionProblem("Tenant non résolu : action d'envoi impossible."));
            }

            var jobId = await PublishTenantSendAsync(scopeFactory, actor.TenantId, actor.CompanyId, ct);

            await activityLogger.LogActivityAsync(
                DocumentActionContract.SendAllEntityType,
                DocumentActionContract.SendAllEntityId,
                DocumentActionContract.SendAllTriggeredActivity,
                string.Create(CultureInfo.InvariantCulture, $"Envoi groupé déclenché par l'opérateur : {count} document(s) prêt(s), montant total {totalGross.ToString("0.00", CultureInfo.InvariantCulture)}."),
                ActorId(actor),
                metadata: new { jobId, count, totalGross },
                companyId: actor.CompanyId,
                cancellationToken: ct);

            return Results.Accepted("/api/v1/documents", new SendAllSummaryResponse(ConfirmationRequired: false, count, totalGross, jobId));
        }).RequireAuthorization(ActionsPermission);

        // POST /api/v1/documents/{id}/resolve-manually — résolution terminale (API02c) : un document Blocked ou
        // RejectedByPa est marqué « traité manuellement hors passerelle » (→ ManuallyHandled). Le MOTIF est
        // OBLIGATOIRE (400 sans motif) car il est inscrit dans la piste d'audit (F06 §3). 404 hors tenant /
        // inexistant ; 409 si l'état courant n'autorise pas la résolution. La transition (état + DocumentEvent
        // append-only) est appliquée par le port IDocumentLifecycle, tenant-scopé et atomique.
        group.MapPost("/{id:guid}/resolve-manually", async (
            Guid id,
            ResolveManuallyRequest? request,
            IDocumentLifecycle lifecycle,
            IActorContextAccessor actorAccessor,
            IActivityLogger activityLogger,
            CancellationToken ct) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Reason))
            {
                return Results.BadRequest(new ActionProblem(
                    "Le motif du traitement manuel est obligatoire : il est journalisé dans la piste d'audit (F06 §3)."));
            }

            var actor = actorAccessor.Current;
            var operatorId = ActorId(actor);

            var outcome = await lifecycle.ResolveManuallyAsync(id, request.Reason, operatorId, ActorName(actor), ct);
            if (outcome is not DocumentResolutionOutcome.Succeeded)
            {
                return ResolutionProblem(outcome, id);
            }

            // Audit (module Audit) en complément du DocumentEvent écrit par le port : l'écriture est awaitée
            // avant la réponse (anti faux-vert) et porte l'identité de l'opérateur.
            await activityLogger.LogActivityAsync(
                DocumentEntityType,
                id.ToString(),
                "documents.resolved_manually",
                string.Create(CultureInfo.InvariantCulture, $"Document {id} marqué « traité manuellement hors passerelle » par l'opérateur. Motif : {request.Reason}"),
                operatorId,
                metadata: new { reason = request.Reason },
                companyId: actor.CompanyId,
                cancellationToken: ct);

            return Results.NoContent();
        }).RequireAuthorization(ActionsPermission);

        // POST /api/v1/documents/{id}/supersede — résolution terminale (API02c) : un document RejectedByPa est
        // lié à son REMPLAÇANT (→ Superseded). Le corps porte l'identifiant du document de remplacement, DÉJÀ reçu
        // via l'agent (le logiciel source est le seul créateur de numéros, F06 §4). 400 si le remplaçant est absent
        // du corps ou identique au document ; 404 si le document rejeté est inconnu ; 409 si l'état n'est pas
        // RejectedByPa ou si le remplaçant n'existe pas dans le tenant.
        group.MapPost("/{id:guid}/supersede", async (
            Guid id,
            SupersedeRequest? request,
            IDocumentLifecycle lifecycle,
            IActorContextAccessor actorAccessor,
            IActivityLogger activityLogger,
            CancellationToken ct) =>
        {
            if (request is null || request.ReplacementDocumentId == Guid.Empty)
            {
                return Results.BadRequest(new ActionProblem(
                    "L'identifiant du document de remplacement est obligatoire (le remplaçant est déjà reçu via l'agent — F06 §4)."));
            }

            if (request.ReplacementDocumentId == id)
            {
                return Results.BadRequest(new ActionProblem(
                    "Un document ne peut pas se remplacer lui-même : indiquez l'identifiant d'un AUTRE document du tenant."));
            }

            var actor = actorAccessor.Current;
            var operatorId = ActorId(actor);

            var outcome = await lifecycle.SupersedeAsync(id, request.ReplacementDocumentId, operatorId, ActorName(actor), ct);
            if (outcome is not DocumentResolutionOutcome.Succeeded)
            {
                return ResolutionProblem(outcome, id);
            }

            await activityLogger.LogActivityAsync(
                DocumentEntityType,
                id.ToString(),
                "documents.superseded",
                string.Create(CultureInfo.InvariantCulture, $"Document {id} (rejeté) lié à son remplaçant {request.ReplacementDocumentId} — passé à l'état Superseded par l'opérateur."),
                operatorId,
                metadata: new { replacementDocumentId = request.ReplacementDocumentId },
                companyId: actor.CompanyId,
                cancellationToken: ct);

            return Results.NoContent();
        }).RequireAuthorization(ActionsPermission);

        // POST /api/v1/documents/{id}/verdict — verdict OPÉRATEUR du garde-fou B2B/B2C (VAL05, F08 §A.4) :
        //   { "verdict": "confirm_b2c" }     → confirme l'acheteur « particulier » (B2C) : enregistre la
        //                                      décision (persistée + journalisée). Le document RESTE Blocked ;
        //                                      la re-vérification le débloque ensuite (verdict posé → recheck).
        //   { "verdict": "handle_manually" } → traite la facture B2B manuellement hors passerelle :
        //                                      Blocked → ManuallyHandled (terminal), via la résolution partagée (API02c).
        // 404 hors tenant (lecture tenant-scopée), 409 si le document n'est pas Blocked, 400 si verdict inconnu.
        group.MapPost("/{id:guid}/verdict", async (
            Guid id,
            VerdictRequest? request,
            IDocumentQueries queries,
            IDocumentLifecycle lifecycle,
            IActorContextAccessor actorAccessor,
            IActivityLogger activityLogger,
            CancellationToken ct) =>
        {
            var verdict = NormalizeVerdict(request?.Verdict);
            if (verdict is null)
            {
                return Results.BadRequest(new ActionProblem(
                    "Verdict inconnu. Valeurs acceptées : « confirm_b2c » (confirmer particulier B2C) ou « handle_manually » (traiter manuellement hors passerelle)."));
            }

            var document = await queries.GetByIdAsync(id, ct);
            if (document is null)
            {
                return Results.NotFound();
            }

            if (!string.Equals(document.State, BlockedState, StringComparison.Ordinal))
            {
                return Results.Conflict(new ActionProblem(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Le verdict du garde-fou B2B/B2C ne s'applique qu'à un document bloqué (document {document.DocumentNumber}, état actuel : {document.State}).")));
            }

            var actor = actorAccessor.Current;
            var operatorId = ActorId(actor);

            if (string.Equals(verdict, VerdictConfirmB2c, StringComparison.Ordinal))
            {
                // Enregistre la décision B2C (persistée + journalisée) SANS changer l'état (la re-vérification débloque).
                await lifecycle.ConfirmBuyerAsIndividualAsync(id, operatorId, ActorName(actor), ct);
                await activityLogger.LogActivityAsync(
                    DocumentEntityType,
                    id.ToString(),
                    DocumentActionContract.VerdictConfirmB2cActivity,
                    string.Create(CultureInfo.InvariantCulture, $"Garde-fou B2B/B2C : acheteur confirmé « particulier » (B2C) par l'opérateur pour le document {document.DocumentNumber} (F08 §A.4). Re-vérifier pour débloquer."),
                    operatorId,
                    metadata: new { document.DocumentNumber, Verdict = VerdictConfirmB2c },
                    companyId: actor.CompanyId,
                    cancellationToken: ct);

                // Le document reste Blocked : on retourne son état courant (inchangé par le verdict).
                return Results.Ok(new VerdictResponse(id, VerdictConfirmB2c, document.State));
            }

            // handle_manually : Blocked → ManuallyHandled (terminal), via la résolution terminale PARTAGÉE (API02c,
            // ResolveManuallyAsync) — une seule mécanique de traitement manuel, motif dérivé du garde-fou (B2B hors
            // passerelle). Le résultat (pas d'exception) mappe proprement un refus concurrent en 4xx (jamais 500).
            var manualOutcome = await lifecycle.ResolveManuallyAsync(id, ManualB2bReason, operatorId, ActorName(actor), ct);
            if (manualOutcome is not DocumentResolutionOutcome.Succeeded)
            {
                return ResolutionProblem(manualOutcome, id);
            }

            await activityLogger.LogActivityAsync(
                DocumentEntityType,
                id.ToString(),
                DocumentActionContract.VerdictHandleManuallyActivity,
                string.Create(CultureInfo.InvariantCulture, $"Garde-fou B2B/B2C : document {document.DocumentNumber} traité manuellement hors passerelle (B2B) par l'opérateur (F08 §A.4)."),
                operatorId,
                metadata: new { document.DocumentNumber, Verdict = VerdictHandleManually },
                companyId: actor.CompanyId,
                cancellationToken: ct);

            return Results.Ok(new VerdictResponse(id, VerdictHandleManually, ManuallyHandledState));
        }).RequireAuthorization(ActionsPermission);

        // POST /api/v1/documents/{id}/recheck — re-vérifie UN document Blocked OU RejectedByPa (CHECK complet :
        // mapping TVA → garde-fou production → validation) sans attendre le prochain traitement : → ReadyToSend
        // s'il passe désormais (table TVA complétée/validée, mentions B2B saisies, verdict B2C posé). Un document
        // rejeté par la PA dont la cause n'est PAS corrigée est TRANSITIONNÉ RejectedByPa → Blocked (il quitte le
        // cul-de-sac pour montrer le motif à corriger — « bloquer plutôt qu'envoyer faux »), tandis qu'un document
        // déjà Blocked reste Blocked (la machine à états interdit Blocked → Blocked) ; dans les deux cas les motifs
        // frais sont renvoyés pour affichage immédiat. DANS TOUS LES CAS, le geste opérateur est tracé dans la
        // piste d'audit append-only (item FIX02). 404 hors tenant, 409 si ni bloqué ni rejeté, ou contenu pivot
        // indisponible.
        group.MapPost("/{id:guid}/recheck", async (
            Guid id,
            IDocumentRecheckService recheckService,
            IActorContextAccessor actorAccessor,
            IActivityLogger activityLogger,
            CancellationToken ct) =>
        {
            var actor = actorAccessor.Current;
            var operatorId = ActorId(actor);
            var result = await recheckService.RecheckAsync(id, operatorId, ActorName(actor), ct);

            switch (result.Outcome)
            {
                case DocumentRecheckOutcome.NotFound:
                    return Results.NotFound();

                case DocumentRecheckOutcome.NotBlocked:
                    return Results.Conflict(new ActionProblem(string.Create(
                        CultureInfo.InvariantCulture,
                        $"La re-vérification ne s'applique qu'à un document bloqué ou rejeté par la Plateforme Agréée (état actuel : {result.State}).")));

                case DocumentRecheckOutcome.ContentUnavailable:
                    return Results.Conflict(new ActionProblem(
                        "Le contenu du document n'est pas disponible pour la re-vérification (pas encore stagé, ou altéré/illisible). Action : relancez l'extraction du document depuis le logiciel source, puis réessayez."));

                default:
                    await activityLogger.LogActivityAsync(
                        DocumentEntityType,
                        id.ToString(),
                        DocumentActionContract.RecheckActivity,
                        string.Create(CultureInfo.InvariantCulture, $"Re-vérification déclenchée par l'opérateur — résultat : « {result.State} »."),
                        operatorId,
                        metadata: new { State = result.State, Outcome = result.Outcome.ToString() },
                        companyId: actor.CompanyId,
                        cancellationToken: ct);

                    return Results.Ok(new RecheckResponse(id, result.State!, result.BlockingReason));
            }
        }).RequireAuthorization(ActionsPermission);

        return app;
    }

    /// <summary>
    /// Normalise le verdict reçu vers sa valeur canonique (<see cref="VerdictConfirmB2c"/> /
    /// <see cref="VerdictHandleManually"/>), insensible à la casse et aux espaces ; <c>null</c> si inconnu.
    /// </summary>
    private static string? NormalizeVerdict(string? verdict)
    {
        if (string.IsNullOrWhiteSpace(verdict))
        {
            return null;
        }

        var normalized = verdict.Trim().ToLowerInvariant();
        return normalized switch
        {
            VerdictConfirmB2c => VerdictConfirmB2c,
            VerdictHandleManually => VerdictHandleManually,
            _ => null,
        };
    }

    /// <summary>
    /// Publie le déclencheur d'envoi MONO-TENANT (<see cref="SendTenantTrigger"/>) sur la queue SYSTÈME
    /// (ADR-0016, INV-API02a-2). Un scope null-tenant FRAIS route <see cref="IJobQueue"/> vers la base
    /// SYSTÈME — exactement le chemin de publication de l'ordonnanceur — et JAMAIS vers la base du tenant
    /// courant (où le job serait orphelin, jamais consommé par le <c>JobWorker</c> null-tenant). Le tenant
    /// cible voyage dans la charge utile ; le handler le rétablit via <c>ITenantScopeFactory.Create</c>.
    /// </summary>
    private static async Task<Guid> PublishTenantSendAsync(
        IServiceScopeFactory scopeFactory,
        string tenantId,
        Guid? companyId,
        CancellationToken ct)
    {
        await using var systemScope = scopeFactory.CreateAsyncScope();
        var queue = systemScope.ServiceProvider.GetRequiredService<IJobQueue>();
        return await queue.EnqueueAsync(new SendTenantTrigger(tenantId, DryRun: false), companyId: companyId, ct: ct);
    }

    /// <summary>Dénombre les <c>ReadyToSend</c> du tenant courant et somme leur montant TTC (decimal — jamais float).</summary>
    private static async Task<(int Count, decimal TotalGross)> SummarizeReadyToSendAsync(
        IDocumentQueries queries,
        CancellationToken ct)
    {
        var count = 0;
        var totalGross = 0m;
        var page = 1;
        while (true)
        {
            var batch = await queries.GetByStateAsync(ReadyToSendState, page, PageSize, ct);
            foreach (var summary in batch)
            {
                count++;
                totalGross += summary.TotalGross;
            }

            if (batch.Count < PageSize)
            {
                break;
            }

            page++;
        }

        return (count, totalGross);
    }

    /// <summary>Identité d'audit de l'opérateur (GUID utilisateur ; « system » si non authentifié, théorique ici).</summary>
    private static string ActorId(IActorContext actor) =>
        actor.IsAuthenticated ? actor.UserId.ToString() : "system";

    /// <summary>
    /// Nom d'affichage de l'opérateur CAPTURÉ AU MOMENT de l'action, persisté avec l'événement d'audit (item
    /// FIX305) pour que la piste reste lisible (« par Marie Comptable » et non un GUID brut), même si le compte
    /// est renommé/supprimé plus tard. Le GUID reste l'identité d'audit stable (<see cref="ActorId"/>) ; ce nom
    /// est un complément (peut être <c>null</c> — la restitution retombe alors sur le GUID en détail technique).
    /// </summary>
    private static string? ActorName(IActorContext actor) =>
        actor.IsAuthenticated ? (actor.DisplayName ?? actor.Email) : null;

    /// <summary>
    /// Mappe le refus d'une résolution terminale (API02c) en réponse HTTP, avec un message opérateur en français
    /// (CLAUDE.md n°12) : 404 si le document est inconnu dans le tenant, 409 si l'état courant ne permet pas la
    /// résolution ou si le remplaçant n'existe pas. <see cref="DocumentResolutionOutcome.Succeeded"/> ne passe
    /// jamais ici (traité par l'appelant avant l'audit).
    /// </summary>
    private static IResult ResolutionProblem(DocumentResolutionOutcome outcome, Guid documentId) => outcome switch
    {
        DocumentResolutionOutcome.DocumentNotFound => Results.NotFound(),
        DocumentResolutionOutcome.InvalidState => Results.Conflict(new ActionProblem(string.Create(
            CultureInfo.InvariantCulture,
            $"Le document {documentId} n'est pas dans un état permettant cette résolution : seul un document bloqué ou rejeté peut être traité manuellement, et seul un document rejeté peut être remplacé."))),
        DocumentResolutionOutcome.ReplacementNotFound => Results.Conflict(new ActionProblem(
            "Le document de remplacement est introuvable dans ce tenant : impossible de lier le rejet à un remplaçant inexistant.")),
        _ => Results.Problem("Résolution impossible."),
    };

    /// <summary>Réponse d'acceptation de l'envoi d'un document (le travail est asynchrone, exécuté par le pipeline).</summary>
    public sealed record SendAcceptedResponse(Guid JobId, Guid DocumentId);

    /// <summary>
    /// Récapitulatif / accusé de l'envoi groupé. <c>ConfirmationRequired</c> = <c>true</c> et <c>JobId</c> =
    /// <c>null</c> tant que l'appelant n'a pas confirmé (<c>?confirm=true</c>) ; sinon <c>JobId</c> porte le
    /// déclencheur publié. <c>TotalGross</c> est en <c>decimal</c> (CLAUDE.md n°1).
    /// </summary>
    public sealed record SendAllSummaryResponse(bool ConfirmationRequired, int Count, decimal TotalGross, Guid? JobId);

    /// <summary>Détail d'erreur d'action (message opérateur en français — CLAUDE.md n°12).</summary>
    public sealed record ActionProblem(string Message);

    /// <summary>Corps de la requête de résolution manuelle (API02c) : le motif OBLIGATOIRE (journalisé, F06 §3).</summary>
    public sealed record ResolveManuallyRequest(string Reason);

    /// <summary>
    /// Corps de la requête de remplacement (API02c) : l'identifiant du document de remplacement, déjà reçu via
    /// l'agent (le logiciel source est le seul créateur de numéros, F06 §4).
    /// </summary>
    public sealed record SupersedeRequest(Guid ReplacementDocumentId);

    /// <summary>Corps de la requête de verdict garde-fou B2B/B2C : <c>confirm_b2c</c> ou <c>handle_manually</c> (F08 §A.4).</summary>
    public sealed record VerdictRequest(string? Verdict);

    /// <summary>Accusé d'un verdict garde-fou : le verdict appliqué et l'état résultant du document.</summary>
    public sealed record VerdictResponse(Guid DocumentId, string Verdict, string State);

    /// <summary>
    /// Résultat d'une re-vérification : l'état résultant (<c>ReadyToSend</c> ou <c>Blocked</c>) et, si le
    /// document reste bloqué, les motifs frais (message opérateur agrégé) pour affichage immédiat (WEB03b).
    /// </summary>
    public sealed record RecheckResponse(Guid DocumentId, string State, string? BlockingReason);
}
