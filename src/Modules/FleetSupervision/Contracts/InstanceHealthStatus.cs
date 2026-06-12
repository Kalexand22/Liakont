namespace Liakont.Modules.FleetSupervision.Contracts;

/// <summary>
/// État de santé technique d'un sous-système d'instance (Host / PostgreSQL / Keycloak), tel que rapporté
/// par le heartbeat d'instance (OPS04, méta-supervision de flotte). Libellé textuel à la sérialisation
/// (robuste à un renumérotage). <see cref="Unknown"/> = sous-système non sondé (ex. pas d'URL de sonde
/// Keycloak configurée) — distinct d'<see cref="Unhealthy"/> (sondé et en échec).
/// </summary>
public enum InstanceHealthStatus
{
    /// <summary>Sous-système non sondé / état indéterminé.</summary>
    Unknown = 0,

    /// <summary>Sous-système sain.</summary>
    Healthy = 1,

    /// <summary>Sous-système dégradé (fonctionne mais avec réserve).</summary>
    Degraded = 2,

    /// <summary>Sous-système sondé et en échec.</summary>
    Unhealthy = 3,
}
