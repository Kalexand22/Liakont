namespace Liakont.Host.Documents;

using System;
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
    /// Re-vérifie un document <b>bloqué</b> (CHECK complet : mapping TVA → garde-fou production →
    /// validation) sans attendre le prochain traitement : <c>Blocked → ReadyToSend</c> s'il passe désormais,
    /// sinon il reste <c>Blocked</c> avec les NOUVEAUX motifs (renvoyés pour affichage immédiat). Renvoie un
    /// RÉSULTAT avec le message opérateur correspondant (jamais d'exception sur un refus métier).
    /// </summary>
    Task<DocumentControlActionResult> RecheckAsync(Guid documentId, CancellationToken cancellationToken = default);
}
