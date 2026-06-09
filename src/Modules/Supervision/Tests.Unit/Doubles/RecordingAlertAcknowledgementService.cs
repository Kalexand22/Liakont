namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Contracts;

/// <summary>
/// Service d'acquittement fictif (dashboard SUP02) : ENREGISTRE les appels (alerte, opérateur) et renvoie un
/// résultat fixe — pour vérifier que l'acquittement est routé dans le scope du BON tenant.
/// </summary>
internal sealed class RecordingAlertAcknowledgementService : IAlertAcknowledgementService
{
    private readonly bool _result;

    public RecordingAlertAcknowledgementService(bool result = true) => _result = result;

    public List<(Guid AlertId, string Operator)> Calls { get; } = [];

    public Task<bool> AcknowledgeAsync(Guid alertId, string operatorIdentity, CancellationToken cancellationToken = default)
    {
        Calls.Add((alertId, operatorIdentity));
        return Task.FromResult(_result);
    }
}
