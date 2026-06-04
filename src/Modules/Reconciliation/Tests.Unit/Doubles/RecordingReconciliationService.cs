namespace Liakont.Modules.Reconciliation.Tests.Unit.Doubles;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Reconciliation.Contracts;
using Liakont.Modules.Reconciliation.Contracts.DTOs;

/// <summary>Service de réconciliation fictif : note l'appel de passe (pour tester le job tenant).</summary>
internal sealed class RecordingReconciliationService : IReconciliationService
{
    public int RunCount { get; private set; }

    public Task<ReconciliationRunResult> RunForCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        RunCount++;
        return Task.FromResult(new ReconciliationRunResult(0, 0, 0, 0));
    }

    public Task ConfirmManualReconciliationAsync(Guid queueEntryId, Guid documentId, string operatorIdentity, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
