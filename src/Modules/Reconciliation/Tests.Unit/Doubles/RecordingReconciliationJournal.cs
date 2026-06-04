namespace Liakont.Modules.Reconciliation.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts.Reconciliation;

/// <summary>Journal de rapprochement fictif : enregistre les appels auto/manuel pour assertion.</summary>
internal sealed class RecordingReconciliationJournal : IDocumentReconciliationJournal
{
    public List<Guid> AutomaticCalls { get; } = [];

    public List<(Guid DocumentId, string Operator)> ManualCalls { get; } = [];

    public Task RecordAutomaticReconciliationAsync(Guid documentId, string detail, CancellationToken cancellationToken = default)
    {
        AutomaticCalls.Add(documentId);
        return Task.CompletedTask;
    }

    public Task RecordManualReconciliationAsync(Guid documentId, string detail, string operatorIdentity, CancellationToken cancellationToken = default)
    {
        ManualCalls.Add((documentId, operatorIdentity));
        return Task.CompletedTask;
    }
}
