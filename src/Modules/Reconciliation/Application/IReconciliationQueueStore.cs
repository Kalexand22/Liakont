namespace Liakont.Modules.Reconciliation.Application;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Reconciliation.Domain;

/// <summary>
/// Port de persistance de la FILE D'ATTENTE de réconciliation (item TRK07), dans la base du
/// tenant. Table MUTABLE (une proposition/un orphelin peut être confirmé après coup) — à distinguer de
/// la piste d'audit append-only. TENANT-SCOPÉ par construction (la connexion EST la base du tenant).
/// </summary>
public interface IReconciliationQueueStore
{
    /// <summary>
    /// Verrou d'avis PostgreSQL (pg_advisory_lock) qui SÉRIALISE le traitement d'un même PDF du pool
    /// entre passes concurrentes (TRK07) ; libéré au Dispose. Retourne un <see cref="IAsyncDisposable"/>
    /// dont le Dispose libère le verrou et la connexion sous-jacente.
    /// </summary>
    Task<System.IAsyncDisposable> AcquireProcessingLockAsync(string poolPdfId, CancellationToken cancellationToken = default);

    /// <summary>Entrée existante pour un PDF du pool (clé <c>PoolPdfId</c>), ou <c>null</c> si non encore traité.</summary>
    Task<ReconciliationQueueEntry?> FindByPoolPdfIdAsync(string poolPdfId, CancellationToken cancellationToken = default);

    /// <summary>Entrée par identifiant de file d'attente, ou <c>null</c>.</summary>
    Task<ReconciliationQueueEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Insère une nouvelle entrée de file d'attente.</summary>
    Task AddAsync(ReconciliationQueueEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Met à jour une entrée existante (ex. confirmation manuelle).</summary>
    Task UpdateAsync(ReconciliationQueueEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Entrées d'un état donné (propositions en attente, orphelins…), triées par date de création.</summary>
    Task<IReadOnlyList<ReconciliationQueueEntry>> ListByStatusAsync(ReconciliationStatus status, CancellationToken cancellationToken = default);

    /// <summary>Identifiants des documents pour lesquels un PDF a été rapproché (auto ou manuel) — base du calcul « documents sans PDF ».</summary>
    Task<IReadOnlyList<Guid>> ListReconciledDocumentIdsAsync(CancellationToken cancellationToken = default);
}
