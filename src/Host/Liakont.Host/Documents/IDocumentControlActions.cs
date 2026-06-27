namespace Liakont.Host.Documents;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Composition des ACTIONS de l'onglet « Contrôles » de la page détail document (WEB03b, F10 §2.3 /
/// F08 §A.4) : verdict du garde-fou B2B/B2C et re-vérification d'un document bloqué. Isole l'orchestration
/// hors de la page Blazor (la page reste présentationnelle, CLAUDE.md n°19) et la rend testable
/// unitairement. C'est le pendant <b>en écriture</b> de <see cref="IDocumentDetailConsoleQueries"/> :
/// la console s'exécute dans son circuit serveur (InteractiveServer) et appelle ce service
/// <b>in-process</b>, exactement comme elle appelle les lectures — elle ne ré-emprunte PAS son propre
/// endpoint HTTP par bouclage (le cookie OIDC fragmenté n'est pas disponible dans le circuit ; précédent
/// WEB05 / <c>IDocumentConsoleActions</c>).
/// <para>
/// Aucune logique fiscale ni machine à états n'est dupliquée : le service réutilise <b>à l'identique</b>
/// le contrat que portent les endpoints API02b (<c>DocumentActionsEndpointMapping</c>) — valider l'état
/// (lecture tenant-scopée), appeler le port <c>IDocumentLifecycle</c> / <c>IDocumentRecheckService</c>, et
/// journaliser l'action de l'opérateur (mêmes codes d'audit, même identité). Les transitions et la
/// re-validation restent dans les modules Documents / Pipeline. TENANT-SCOPÉ par construction (la connexion
/// EST le tenant, CLAUDE.md n°9/17).
/// </para>
/// </summary>
internal interface IDocumentControlActions
{
    /// <summary>
    /// Pose le verdict du garde-fou B2B/B2C sur un document <b>bloqué</b> (F08 §A.4). Renvoie un RÉSULTAT
    /// (jamais d'exception sur un refus métier) avec un message opérateur en français citant le numéro de
    /// document (CLAUDE.md n°12). <see cref="ConsoleVerdict.ConfirmIndividualB2c"/> enregistre la décision
    /// « particulier (B2C) » SANS changer l'état (la re-vérification débloque ensuite) ;
    /// <see cref="ConsoleVerdict.HandleManuallyB2b"/> traite la facture B2B hors passerelle
    /// (<c>Blocked → ManuallyHandled</c>, terminal, via la résolution partagée).
    /// </summary>
    Task<DocumentControlActionResult> SubmitVerdictAsync(
        Guid documentId, ConsoleVerdict verdict, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-vérifie un document <b>bloqué</b> ou <b>rejeté par la Plateforme Agréée</b> (CHECK complet : mapping
    /// TVA → garde-fou production → validation) sans attendre le prochain traitement : → <c>ReadyToSend</c> s'il
    /// passe désormais (cause corrigée). Un document <c>RejectedByPa</c> dont la cause n'est PAS corrigée est
    /// transitionné vers <c>Blocked</c> (il quitte le cul-de-sac pour montrer le motif à corriger) ; un document
    /// déjà <c>Blocked</c> reste <c>Blocked</c>. Les NOUVEAUX motifs sont renvoyés pour affichage immédiat.
    /// Renvoie un RÉSULTAT avec le message opérateur correspondant (jamais d'exception sur un refus métier).
    /// </summary>
    Task<DocumentControlActionResult> RecheckAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-vérifie EN MASSE les documents <paramref name="documentIds"/> du tenant courant (FIX207/FIX302 — action
    /// « Revérifier la sélection » de la barre de sélection et action globale « Revérifier tout » de la barre d'outils).
    /// Porte la même garde de
    /// permission (<c>liakont.actions</c>, défense en profondeur) et délègue la boucle + la décision de blocage au
    /// cœur <see cref="Liakont.Modules.Pipeline.Contracts.IDocumentRecheckService.RecheckManyAsync"/> (source
    /// unique, trace d'audit FIX02 par document). Renvoie un RÉSULTAT avec un message opérateur en français portant
    /// les compteurs (« N débloqués, N restés bloqués ») — jamais d'exception sur un refus de permission.
    /// TENANT-SCOPÉ par construction (la connexion EST le tenant).
    /// </summary>
    Task<DocumentBulkRecheckResult> RecheckManyAsync(
        IReadOnlyList<Guid> documentIds, CancellationToken cancellationToken = default);
}
