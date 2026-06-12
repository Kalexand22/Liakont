namespace Liakont.Modules.FleetSupervision.Infrastructure;

/// <summary>
/// Déclencheur du job SYSTÈME d'envoi de télémétrie d'instance (OPS04). Planifié par le module <c>Job</c> ;
/// la fréquence est un paramétrage de déploiement (geste opérateur via l'admin des planifications, comme
/// l'évaluation de supervision ou l'ancrage TRK06). Marqueur sans donnée : son handler collecte l'état
/// courant de l'instance et le publie au central. Job d'INSTANCE (pas de fan-out par tenant).
/// </summary>
public sealed record InstanceHeartbeatTrigger;
