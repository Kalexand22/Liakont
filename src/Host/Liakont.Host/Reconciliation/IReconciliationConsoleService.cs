namespace Liakont.Host.Reconciliation;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Composition console de la page Réconciliation (WEB08). Isole la page Blazor de l'accès au module
/// Reconciliation (TRK07/API04) : lecture des trois files et actions opérateur (confirmer / rejeter une
/// proposition, lier manuellement un PDF à un document). Appelle les contrats du module IN-PROCESS depuis le
/// circuit serveur — jamais l'endpoint HTTP (le cookie OIDC n'est pas disponible pour boucler sur l'API, même
/// motif que <c>DocumentControlActionsService</c>/WEB05). Tenant-scopé par construction (la connexion EST le
/// tenant). La garde de permission (<c>liakont.actions</c>) est appliquée ici (défense en profondeur) en plus
/// du masquage des boutons côté UI.
/// </summary>
internal interface IReconciliationConsoleService
{
    /// <summary>
    /// Charge les trois files de réconciliation du tenant courant (propositions, orphelins, documents sans
    /// PDF). Lecture seule, sans effet de bord.
    /// </summary>
    Task<ReconciliationQueueViewModel> GetQueueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// CONFIRME une proposition : rattache le PDF au document que le moteur a proposé (le serveur fait foi).
    /// Refuse si l'opérateur ne porte pas <c>liakont.actions</c>. Renvoie un message opérateur, jamais une
    /// exception.
    /// </summary>
    Task<ReconciliationActionResult> ConfirmProposalAsync(Guid queueEntryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// REJETTE une proposition : le PDF redevient un orphelin en file manuelle (aucun rapprochement créé).
    /// Refuse sans <c>liakont.actions</c>. Renvoie un message opérateur, jamais une exception.
    /// </summary>
    Task<ReconciliationActionResult> RejectProposalAsync(Guid queueEntryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// LIE MANUELLEMENT le PDF d'une entrée de file (proposition ou orphelin) au document choisi par
    /// l'opérateur. Refuse sans <c>liakont.actions</c>. Renvoie un message opérateur, jamais une exception.
    /// </summary>
    Task<ReconciliationActionResult> LinkManuallyAsync(Guid queueEntryId, Guid documentId, CancellationToken cancellationToken = default);
}
