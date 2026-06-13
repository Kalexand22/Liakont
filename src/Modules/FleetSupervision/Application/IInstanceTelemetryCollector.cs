namespace Liakont.Modules.FleetSupervision.Application;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.FleetSupervision.Contracts;

/// <summary>
/// Collecte la télémétrie TECHNIQUE locale d'une instance (OPS04) — version, santé (Host/PostgreSQL/Keycloak),
/// nombre de tenants, espace disque, dernière sauvegarde réussie — et la met sous forme de
/// <see cref="InstanceHeartbeatReport"/> prêt à publier. Ne collecte JAMAIS de donnée métier d'un éditeur.
/// </summary>
public interface IInstanceTelemetryCollector
{
    /// <summary>Assemble le heartbeat de l'instance courante.</summary>
    Task<InstanceHeartbeatReport> CollectAsync(CancellationToken cancellationToken = default);
}
