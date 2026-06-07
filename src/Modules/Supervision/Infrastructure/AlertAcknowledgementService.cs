namespace Liakont.Modules.Supervision.Infrastructure;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Contracts;

/// <summary>
/// Acquittement d'une alerte (item SUP01a) sur la base DU TENANT courant. Acquitter n'affecte PAS la
/// résolution (toujours automatique quand la condition disparaît) : c'est un marqueur « prise en charge »,
/// journalisé par l'identité de l'opérateur. Retourne <c>false</c> si l'alerte est absente (pas d'exception
/// — l'UI affichera un message ; aucune mutation silencieuse).
/// </summary>
internal sealed class AlertAcknowledgementService : IAlertAcknowledgementService
{
    private readonly IAlertStore _store;
    private readonly TimeProvider _timeProvider;

    public AlertAcknowledgementService(IAlertStore store)
        : this(store, TimeProvider.System)
    {
    }

    internal AlertAcknowledgementService(IAlertStore store, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _store = store;
        _timeProvider = timeProvider;
    }

    public async Task<bool> AcknowledgeAsync(Guid alertId, string operatorIdentity, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorIdentity);

        var alert = await _store.GetByIdAsync(alertId, cancellationToken).ConfigureAwait(false);
        if (alert is null)
        {
            return false;
        }

        alert.Acknowledge(operatorIdentity, _timeProvider.GetUtcNow());
        await _store.AcknowledgeAsync(alert, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
