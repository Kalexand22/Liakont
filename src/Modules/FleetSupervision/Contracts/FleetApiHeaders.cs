namespace Liakont.Modules.FleetSupervision.Contracts;

/// <summary>
/// Constantes de transport du heartbeat de flotte (OPS04), partagées par l'émetteur (job d'envoi côté
/// instance) et le récepteur (endpoint central du Host).
/// </summary>
public static class FleetApiHeaders
{
    /// <summary>En-tête portant la clé d'ingestion partagée du heartbeat de flotte.</summary>
    public const string Key = "X-Fleet-Key";

    /// <summary>Chemin de l'endpoint central de réception des heartbeats.</summary>
    public const string HeartbeatPath = "/api/fleet/v1/heartbeat";
}
