namespace Liakont.Modules.FleetSupervision.Contracts;

/// <summary>
/// Charge utile du heartbeat d'instance (OPS04, méta-supervision de flotte) : une instance opérée (ou
/// self-hosted avec souscription) la POST périodiquement à l'endpoint central d'IT Innovations.
/// <para>
/// CLOISONNEMENT ÉDITEUR STRICT (acceptance OPS04, vérifié par test) : ce contrat ne transporte QUE de la
/// télémétrie TECHNIQUE. Il ne contient JAMAIS de donnée métier d'un éditeur — pas de nom de tenant, pas de
/// SIREN, pas de compteur de documents, aucun montant. <see cref="TenantCount"/> est un simple ENTIER (le
/// nombre de tenants), jamais leur identité. Ajouter ici un champ portant une donnée métier romprait le
/// cloisonnement et ferait échouer <c>InstanceHeartbeatReportIsolationTests</c>.
/// </para>
/// </summary>
public sealed record InstanceHeartbeatReport
{
    /// <summary>Identifiant opaque de l'instance (paramétrage de déploiement) — jamais un tenant ni un SIREN.</summary>
    public string InstanceId { get; init; } = string.Empty;

    /// <summary>Libellé d'affichage de l'instance (nom technique choisi par l'opérateur, ex. « azmut-prod-3 »).</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Mode d'hébergement (opérée / self-hosted) — pilote la notification de mise à jour.</summary>
    public InstanceHostingMode HostingMode { get; init; }

    /// <summary>Version de la plateforme rapportée par l'instance (ex. « 1.4.0 »).</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Santé du Host (état de santé global des sondes applicatives).</summary>
    public InstanceHealthStatus HostHealth { get; init; }

    /// <summary>Santé de PostgreSQL.</summary>
    public InstanceHealthStatus DatabaseHealth { get; init; }

    /// <summary>Santé de Keycloak (Unknown si aucune sonde n'est configurée).</summary>
    public InstanceHealthStatus KeycloakHealth { get; init; }

    /// <summary>NOMBRE de tenants actifs (un entier — jamais leurs noms ni leurs données).</summary>
    public int TenantCount { get; init; }

    /// <summary>Espace disque libre, en octets, du volume de données de l'instance.</summary>
    public long DiskFreeBytes { get; init; }

    /// <summary>Espace disque total, en octets, du volume de données de l'instance.</summary>
    public long DiskTotalBytes { get; init; }

    /// <summary>Horodatage UTC de la dernière sauvegarde réussie connue (null si inconnue / jamais réussie).</summary>
    public DateTimeOffset? LastSuccessfulBackupUtc { get; init; }

    /// <summary>
    /// Adresse de contact TECHNIQUE de l'instance self-hosted (destinataire de l'email « nouvelle version
    /// disponible »). Contact d'exploitation de l'éditeur, jamais une donnée fiscale d'un tenant.
    /// </summary>
    public string? ContactEmail { get; init; }

    /// <summary>Horodatage UTC d'émission du heartbeat par l'instance.</summary>
    public DateTimeOffset SentAtUtc { get; init; }
}
