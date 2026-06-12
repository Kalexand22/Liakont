namespace Liakont.Modules.FleetSupervision.Application;

/// <summary>Paramétrage du rôle CENTRAL de la méta-supervision de flotte (OPS04).</summary>
public sealed class FleetCentralOptions
{
    /// <summary>Active la réception des heartbeats, le dashboard de flotte et la notification de mise à jour.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Clé d'ingestion partagée attendue dans l'en-tête <c>X-Fleet-Key</c> des heartbeats entrants. SECRET de
    /// déploiement (jamais versionné en clair — CLAUDE.md n°10) ; vide = aucun heartbeat n'est accepté.
    /// </summary>
    public string IngestionKey { get; init; } = string.Empty;

    /// <summary>Dernière version de plateforme publiée — référence de l'alerte « version obsolète ».</summary>
    public string LatestVersion { get; init; } = string.Empty;

    /// <summary>Seuil de silence (minutes) au-delà duquel une instance est déclarée muette. Défaut 30.</summary>
    public int InstanceMuteThresholdMinutes { get; init; } = 30;

    /// <summary>Âge maximal (heures) d'une sauvegarde réussie avant l'alerte « sauvegarde en échec ». Défaut 26.</summary>
    public int BackupMaxAgeHours { get; init; } = 26;
}
