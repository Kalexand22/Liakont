namespace Liakont.Modules.FleetSupervision.Contracts.DTOs;

/// <summary>
/// Vue d'une instance de la flotte pour le dashboard d'IT Innovations (OPS04) : dernière télémétrie connue
/// d'une instance, lue depuis le magasin central. Strictement technique (même cloisonnement que
/// <see cref="InstanceHeartbeatReport"/>) — aucune donnée métier d'éditeur.
/// </summary>
public sealed record FleetInstanceDto
{
    /// <summary>Identifiant opaque de l'instance.</summary>
    public string InstanceId { get; init; } = string.Empty;

    /// <summary>Libellé d'affichage de l'instance.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Mode d'hébergement.</summary>
    public InstanceHostingMode HostingMode { get; init; }

    /// <summary>Dernière version rapportée.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Santé du Host.</summary>
    public InstanceHealthStatus HostHealth { get; init; }

    /// <summary>Santé de PostgreSQL.</summary>
    public InstanceHealthStatus DatabaseHealth { get; init; }

    /// <summary>Santé de Keycloak.</summary>
    public InstanceHealthStatus KeycloakHealth { get; init; }

    /// <summary>Nombre de tenants actifs.</summary>
    public int TenantCount { get; init; }

    /// <summary>Espace disque libre (octets).</summary>
    public long DiskFreeBytes { get; init; }

    /// <summary>Espace disque total (octets).</summary>
    public long DiskTotalBytes { get; init; }

    /// <summary>Dernière sauvegarde réussie connue (null si inconnue).</summary>
    public DateTimeOffset? LastSuccessfulBackupUtc { get; init; }

    /// <summary>Contact technique de l'instance (self-hosted).</summary>
    public string? ContactEmail { get; init; }

    /// <summary>Premier heartbeat reçu (UTC).</summary>
    public DateTimeOffset FirstSeenUtc { get; init; }

    /// <summary>Dernier heartbeat reçu (UTC) — base du calcul « instance muette ».</summary>
    public DateTimeOffset LastSeenUtc { get; init; }
}
