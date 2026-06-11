namespace Liakont.Modules.Supervision.Application;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Domain;

/// <summary>
/// Notifieur inerte (Null Object). Défaut des constructeurs du moteur d'évaluation qui ne prennent pas de
/// notifieur (tests SUP01a/SUP01b, où la notification n'est pas le sujet) : le moteur n'a jamais à tester
/// la présence d'un notifieur. N'envoie rien, ne lève jamais.
/// </summary>
public sealed class NullAlertNotifier : IAlertNotifier
{
    /// <summary>Instance partagée (sans état).</summary>
    public static readonly NullAlertNotifier Instance = new();

    private NullAlertNotifier()
    {
    }

    public Task NotifyRaisedAsync(Alert alert, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyResolvedAsync(Alert alert, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
