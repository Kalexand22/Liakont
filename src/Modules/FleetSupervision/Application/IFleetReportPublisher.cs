namespace Liakont.Modules.FleetSupervision.Application;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.FleetSupervision.Contracts;

/// <summary>
/// Publie un heartbeat d'instance vers l'endpoint central de la flotte (OPS04). L'implémentation HTTP n'est
/// PAS bloquante : un échec de transport est journalisé et avalé (le central détectera l'instance muette à
/// l'absence de heartbeats) — un échec d'envoi de télémétrie ne doit jamais mettre une instance en panne.
/// </summary>
public interface IFleetReportPublisher
{
    /// <summary>Envoie le heartbeat au central.</summary>
    Task PublishAsync(InstanceHeartbeatReport report, CancellationToken cancellationToken = default);
}
