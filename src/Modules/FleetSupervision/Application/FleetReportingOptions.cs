namespace Liakont.Modules.FleetSupervision.Application;

using Liakont.Modules.FleetSupervision.Contracts;

/// <summary>Paramétrage du rôle REPORTING de la méta-supervision de flotte (OPS04).</summary>
public sealed class FleetReportingOptions
{
    /// <summary>Active l'envoi périodique de la télémétrie d'instance au central.</summary>
    public bool Enabled { get; init; }

    /// <summary>URL de base de l'endpoint central (ex. <c>https://fleet.it-innovations.fr</c>).</summary>
    public string CentralUrl { get; init; } = string.Empty;

    /// <summary>Identifiant opaque de cette instance dans la flotte.</summary>
    public string InstanceId { get; init; } = string.Empty;

    /// <summary>Libellé d'affichage de cette instance (repli sur l'identifiant si vide).</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Mode d'hébergement de cette instance.</summary>
    public InstanceHostingMode HostingMode { get; init; }

    /// <summary>
    /// Clé d'ingestion (en-tête <c>X-Fleet-Key</c>) présentée au central. SECRET de déploiement (jamais
    /// versionné en clair — CLAUDE.md n°10).
    /// </summary>
    public string FleetKey { get; init; } = string.Empty;

    /// <summary>Contact technique (self-hosted) destinataire de l'email « nouvelle version disponible ».</summary>
    public string? ContactEmail { get; init; }

    /// <summary>
    /// Chemin d'un fichier marqueur que le job de sauvegarde TOUCHE à chaque succès. Sa date de dernière
    /// modification sert de « dernière sauvegarde réussie ». Vide / introuvable = inconnue (déclenche
    /// l'alerte côté central). Évite de coupler la télémétrie aux internes d'OPS01b.
    /// </summary>
    public string? BackupMarkerPath { get; init; }

    /// <summary>
    /// URL d'une sonde Keycloak (ex. l'endpoint <c>.well-known/openid-configuration</c> du realm). Vide =
    /// santé Keycloak rapportée comme <see cref="InstanceHealthStatus.Unknown"/>.
    /// </summary>
    public string? KeycloakProbeUrl { get; init; }

    /// <summary>Chemin dont on rapporte l'espace disque (défaut : répertoire courant de l'instance).</summary>
    public string? DataPath { get; init; }
}
