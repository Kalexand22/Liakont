namespace Liakont.Modules.FleetSupervision.Domain;

using System;
using Liakont.Modules.FleetSupervision.Contracts;

/// <summary>
/// État connu d'une instance de la flotte côté CENTRAL (OPS04), construit à partir d'un heartbeat reçu.
/// État opérationnel MUTABLE (mis à jour à chaque heartbeat), persisté dans la base SYSTÈME du central —
/// jamais une table d'audit. <see cref="FirstSeenUtc"/> et <see cref="NotifiedVersion"/> sont préservés par
/// l'upsert du magasin (un nouveau heartbeat ne les réécrit pas) : ils tracent l'historique de la flotte et
/// l'anti-rebond de la notification de mise à jour.
/// </summary>
public sealed class FleetInstance
{
    private FleetInstance(
        string instanceId,
        string displayName,
        InstanceHostingMode hostingMode,
        string version,
        InstanceHealthStatus hostHealth,
        InstanceHealthStatus databaseHealth,
        InstanceHealthStatus keycloakHealth,
        int tenantCount,
        long diskFreeBytes,
        long diskTotalBytes,
        DateTimeOffset? lastSuccessfulBackupUtc,
        string? contactEmail,
        DateTimeOffset firstSeenUtc,
        DateTimeOffset lastSeenUtc)
    {
        InstanceId = instanceId;
        DisplayName = displayName;
        HostingMode = hostingMode;
        Version = version;
        HostHealth = hostHealth;
        DatabaseHealth = databaseHealth;
        KeycloakHealth = keycloakHealth;
        TenantCount = tenantCount;
        DiskFreeBytes = diskFreeBytes;
        DiskTotalBytes = diskTotalBytes;
        LastSuccessfulBackupUtc = lastSuccessfulBackupUtc;
        ContactEmail = contactEmail;
        FirstSeenUtc = firstSeenUtc;
        LastSeenUtc = lastSeenUtc;
    }

    /// <summary>Identifiant opaque de l'instance.</summary>
    public string InstanceId { get; }

    /// <summary>Libellé d'affichage (repli sur l'identifiant si vide).</summary>
    public string DisplayName { get; }

    /// <summary>Mode d'hébergement.</summary>
    public InstanceHostingMode HostingMode { get; }

    /// <summary>Version rapportée.</summary>
    public string Version { get; }

    /// <summary>Santé du Host.</summary>
    public InstanceHealthStatus HostHealth { get; }

    /// <summary>Santé de PostgreSQL.</summary>
    public InstanceHealthStatus DatabaseHealth { get; }

    /// <summary>Santé de Keycloak.</summary>
    public InstanceHealthStatus KeycloakHealth { get; }

    /// <summary>Nombre de tenants actifs.</summary>
    public int TenantCount { get; }

    /// <summary>Espace disque libre (octets).</summary>
    public long DiskFreeBytes { get; }

    /// <summary>Espace disque total (octets).</summary>
    public long DiskTotalBytes { get; }

    /// <summary>Dernière sauvegarde réussie connue (null si inconnue).</summary>
    public DateTimeOffset? LastSuccessfulBackupUtc { get; }

    /// <summary>Contact technique (self-hosted).</summary>
    public string? ContactEmail { get; }

    /// <summary>Premier heartbeat reçu (préservé par l'upsert).</summary>
    public DateTimeOffset FirstSeenUtc { get; }

    /// <summary>Dernier heartbeat reçu.</summary>
    public DateTimeOffset LastSeenUtc { get; }

    /// <summary>
    /// Construit l'état d'instance à partir d'un heartbeat reçu à <paramref name="nowUtc"/>. Valide
    /// l'identifiant (non vide), normalise le libellé (repli sur l'identifiant) et borne les valeurs
    /// négatives aberrantes à zéro. <see cref="FirstSeenUtc"/> et <see cref="LastSeenUtc"/> sont initialisés
    /// à <paramref name="nowUtc"/> ; à l'upsert, <c>first_seen_utc</c> existant est préservé.
    /// </summary>
    public static FleetInstance Register(InstanceHeartbeatReport report, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(report);

        string instanceId = report.InstanceId?.Trim() ?? string.Empty;
        if (instanceId.Length == 0)
        {
            throw new ArgumentException("Le heartbeat d'instance doit porter un identifiant d'instance non vide.", nameof(report));
        }

        string displayName = string.IsNullOrWhiteSpace(report.DisplayName) ? instanceId : report.DisplayName.Trim();
        string version = report.Version?.Trim() ?? string.Empty;
        string? contactEmail = string.IsNullOrWhiteSpace(report.ContactEmail) ? null : report.ContactEmail.Trim();

        return new FleetInstance(
            instanceId,
            displayName,
            report.HostingMode,
            version,
            report.HostHealth,
            report.DatabaseHealth,
            report.KeycloakHealth,
            Math.Max(0, report.TenantCount),
            Math.Max(0L, report.DiskFreeBytes),
            Math.Max(0L, report.DiskTotalBytes),
            report.LastSuccessfulBackupUtc,
            contactEmail,
            nowUtc,
            nowUtc);
    }
}
