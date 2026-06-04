namespace Liakont.Modules.Reconciliation.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Reconciliation.Application;
using Liakont.Modules.Reconciliation.Domain;

/// <summary>File d'attente de réconciliation en mémoire pour les tests unitaires du service.</summary>
internal sealed class InMemoryReconciliationQueueStore : IReconciliationQueueStore
{
    private readonly List<ReconciliationQueueEntry> _entries = [];

    public IReadOnlyList<ReconciliationQueueEntry> Entries => _entries;

    public Task<IAsyncDisposable> AcquireProcessingLockAsync(string poolPdfId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IAsyncDisposable>(new NoopLock());

    public Task<ReconciliationQueueEntry?> FindByPoolPdfIdAsync(string poolPdfId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_entries.FirstOrDefault(e => e.PoolPdfId == poolPdfId));

    public Task<ReconciliationQueueEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_entries.FirstOrDefault(e => e.Id == id));

    public Task AddAsync(ReconciliationQueueEntry entry, CancellationToken cancellationToken = default)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ReconciliationQueueEntry entry, CancellationToken cancellationToken = default)
    {
        // L'instance est mutée en place dans ces tests ; rien à réécrire.
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReconciliationQueueEntry>> ListByStatusAsync(ReconciliationStatus status, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReconciliationQueueEntry>>(_entries.Where(e => e.Status == status).ToList());

    public Task<IReadOnlyList<Guid>> ListReconciledDocumentIdsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> ids = _entries
            .Where(e => e.Status is ReconciliationStatus.ReconciledAuto or ReconciliationStatus.ReconciledManual && e.ProposedDocumentId is not null)
            .Select(e => e.ProposedDocumentId!.Value)
            .Distinct()
            .ToList();
        return Task.FromResult(ids);
    }

    private sealed class NoopLock : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
