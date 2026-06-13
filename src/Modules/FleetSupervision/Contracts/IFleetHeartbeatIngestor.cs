namespace Liakont.Modules.FleetSupervision.Contracts;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Réception d'un heartbeat d'instance côté CENTRAL (OPS04). Consommée par l'endpoint de la flotte du Host
/// (<c>POST /api/fleet/v1/heartbeat</c>, authentifié par clé d'ingestion). Idempotent par instance : un
/// nouveau heartbeat met à jour l'état connu (upsert), il ne crée jamais de doublon.
/// </summary>
public interface IFleetHeartbeatIngestor
{
    /// <summary>Enregistre (ou met à jour) l'état connu de l'instance d'après le heartbeat reçu.</summary>
    Task RecordAsync(InstanceHeartbeatReport report, CancellationToken cancellationToken = default);
}
