namespace Liakont.Host.Documents;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Composition des ACTIONS D'ENVOI de la page Documents (WEB05, F10 §2.1) : envoi des documents sélectionnés,
/// envoi groupé « Tout envoyer » (avec récapitulatif de confirmation) et déclenchement manuel d'un traitement.
/// Isole l'orchestration hors de la page Blazor (la page reste présentationnelle, CLAUDE.md n°19) et la rend
/// testable unitairement. La console s'exécute dans son circuit serveur (InteractiveServer) et appelle ce
/// service <b>in-process</b> — elle ne ré-emprunte PAS ses propres endpoints HTTP par bouclage (le cookie OIDC
/// fragmenté n'est pas disponible dans le circuit ; précédent WEB03b / <c>IDocumentControlActions</c>).
/// <para>
/// Aucune logique fiscale ni machine à états n'est dupliquée et AUCUN second chemin d'envoi n'est ouvert
/// (interdit — il dédoublerait la logique fiscale) : le service réutilise <b>à l'identique</b> le contrat des
/// endpoints API02a (<c>DocumentActionsEndpointMapping</c>) et runs/trigger (<c>PipelineEndpointMapping</c>) —
/// valider l'état (lecture tenant-scopée), <b>publier</b> le déclencheur mono-tenant <c>SendTenantTrigger</c>
/// sur la queue SYSTÈME (ADR-0016), et journaliser l'action de l'opérateur (mêmes codes d'audit via les
/// SOURCES UNIQUES <c>DocumentActionContract</c> / <c>PipelineRunActionContract</c>, même identité). L'envoi
/// proprement dit (lecture du pivot stagé, anti-doublon, archive WORM, transitions) reste dans le pipeline.
/// TENANT-SCOPÉ par construction (la connexion EST le tenant, CLAUDE.md n°9/17).
/// </para>
/// </summary>
internal interface IDocumentSendActions
{
    /// <summary>
    /// Déclenche l'envoi des documents SÉLECTIONNÉS : chaque document est relu (tenant-scopé) et validé
    /// <c>ReadyToSend</c> (mêmes gardes que l'endpoint <c>POST /documents/{id}/send</c>), puis le traitement
    /// d'envoi du tenant est déclenché UNE FOIS (ADR-0016 : <c>SendTenantJob</c> émet TOUS les
    /// <c>ReadyToSend</c> — il boucle sur l'état, pas sur l'id ; publier un déclencheur par document serait
    /// redondant). Chaque document prêt est journalisé (<c>documents.send_triggered</c>). Renvoie un RÉSULTAT
    /// agrégé avec un message opérateur en français (documents déclenchés / ignorés avec le motif et le numéro).
    /// </summary>
    Task<DocumentSendActionResult> SendSelectionAsync(
        IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récapitule les documents prêts à l'envoi du tenant courant (nombre + montant total TTC) pour la
    /// confirmation du « Tout envoyer » — lecture tenant-scopée, AUCUNE écriture, AUCUN job publié (miroir de
    /// <c>POST /documents/send-all?confirm=false</c>).
    /// </summary>
    Task<DocumentSendSummary> SummarizeReadyToSendAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Déclenche l'envoi groupé de TOUS les <c>ReadyToSend</c> du tenant courant (miroir de
    /// <c>POST /documents/send-all?confirm=true</c>) : publie le déclencheur d'envoi tenant-scopé et journalise
    /// l'action (<c>documents.send_all_triggered</c>). Renvoie un RÉSULTAT avec un message opérateur en français.
    /// </summary>
    Task<DocumentSendActionResult> SendAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Déclenche manuellement un traitement du tenant courant (« Lancer un traitement maintenant », miroir de
    /// <c>POST /runs/trigger</c>) : publie le déclencheur mono-tenant et journalise l'action
    /// (<c>pipeline.run_triggered</c>). Renvoie un RÉSULTAT avec un message opérateur en français.
    /// </summary>
    Task<DocumentSendActionResult> TriggerRunAsync(CancellationToken cancellationToken = default);
}
