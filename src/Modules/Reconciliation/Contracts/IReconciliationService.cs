namespace Liakont.Modules.Reconciliation.Contracts;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Reconciliation.Contracts.DTOs;

/// <summary>
/// Surface publique d'ACTION du module Reconciliation (item TRK07). Rapproche les PDF du pool non
/// lié des documents émis du tenant et applique les effets (addendum d'archive WORM, fait d'audit,
/// mise à jour de la file d'attente). TENANT-SCOPÉE par construction (la base et le coffre routent vers
/// le tenant courant — blueprint §7). Consommée par le job de réconciliation (module Job, à la demande
/// ou après réception de PDF du pool) et par la console (API04/WEB08).
/// </summary>
public interface IReconciliationService
{
    /// <summary>
    /// Exécute une passe de réconciliation pour le TENANT COURANT : énumère les PDF du pool non encore
    /// traités, applique les trois stratégies (TRK07), lie automatiquement les correspondances de
    /// confiance haute (addendum + audit), propose les confiances moyennes, classe le reste en orphelins.
    /// </summary>
    Task<ReconciliationRunResult> RunForCurrentTenantAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirme un rapprochement MANUEL : l'opérateur rattache le PDF d'une entrée de file d'attente
    /// (proposition de confiance moyenne ou orphelin) au document <paramref name="documentId"/>. Ajoute le
    /// PDF au paquet d'archive en addendum (WORM), inscrit un <c>DocumentReconciledManual</c> avec
    /// l'identité de l'opérateur, et passe l'entrée à l'état rapproché. Lève si l'entrée est déjà rapprochée.
    /// </summary>
    Task ConfirmManualReconciliationAsync(Guid queueEntryId, Guid documentId, string operatorIdentity, CancellationToken cancellationToken = default);

    /// <summary>
    /// CONFIRME une PROPOSITION (console API04) : rattache le PDF au document que le moteur a PROPOSÉ pour
    /// cette entrée (le serveur fait foi — l'opérateur accepte la correspondance affichée, il ne choisit pas
    /// la cible). Délègue à <see cref="ConfirmManualReconciliationAsync"/> avec le document proposé. Lève une
    /// <c>NotFoundException</c> si l'entrée n'existe pas dans le tenant, une <c>ConflictException</c> si ce
    /// n'est pas une proposition en attente.
    /// </summary>
    Task ConfirmProposalAsync(Guid queueEntryId, string operatorIdentity, CancellationToken cancellationToken = default);

    /// <summary>
    /// REJETTE une PROPOSITION (console API04) : l'opérateur décline la correspondance proposée. Aucun
    /// addendum WORM n'est créé (le PDF n'est pas rapproché) ; l'entrée REDEVIENT un orphelin en file
    /// manuelle, avec l'identité de l'opérateur conservée. Lève une <c>NotFoundException</c> si l'entrée
    /// n'existe pas dans le tenant, une <c>ConflictException</c> si ce n'est pas une proposition en attente.
    /// </summary>
    Task RejectProposalAsync(Guid queueEntryId, string operatorIdentity, CancellationToken cancellationToken = default);
}
