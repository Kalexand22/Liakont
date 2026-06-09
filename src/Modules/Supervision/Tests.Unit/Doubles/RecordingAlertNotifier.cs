namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Domain;

/// <summary>
/// Notifieur d'alertes factice : enregistre les alertes notifiées au déclenchement / à la résolution, pour
/// vérifier que le moteur notifie aux SEULES transitions (anti-spam).
/// </summary>
internal sealed class RecordingAlertNotifier : IAlertNotifier
{
    private readonly List<Alert> _raised = new();
    private readonly List<Alert> _resolved = new();

    public IReadOnlyList<Alert> Raised => _raised;

    public IReadOnlyList<Alert> Resolved => _resolved;

    public Task NotifyRaisedAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        _raised.Add(alert);
        return Task.CompletedTask;
    }

    public Task NotifyResolvedAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        _resolved.Add(alert);
        return Task.CompletedTask;
    }
}
