namespace Liakont.Agent.Core.Update;

using System;

/// <summary>
/// Tout ce dont l'updater détaché a besoin pour remplacer les binaires et restaurer en cas d'échec
/// (ADR-0013). C'est le CONTRAT agent ↔ updater (passé en arguments de ligne de commande) : aucun type
/// partagé, couplage faible — l'updater est un exe autonome qui survit à l'arrêt du service.
/// </summary>
public sealed class UpdaterLaunchRequest
{
    /// <summary>Crée une requête de lancement de l'updater.</summary>
    /// <param name="targetVersion">Version installée (journalisée et reportée dans le statut).</param>
    /// <param name="stagingDirectory">Dossier des nouveaux binaires VÉRIFIÉS (signature + hash).</param>
    /// <param name="installDirectory">Dossier d'installation à remplacer.</param>
    /// <param name="backupDirectory">Dossier de sauvegarde des binaires courants (rollback).</param>
    /// <param name="serviceName">Nom du service Windows à arrêter/redémarrer.</param>
    /// <param name="healthTimeout">Budget d'attente du redémarrage SAIN avant rollback.</param>
    /// <param name="logPath">Fichier de log de l'updater.</param>
    /// <param name="statusPath">Fichier de statut écrit par l'updater (relu par l'agent).</param>
    /// <param name="heartbeatMarkerPath">Marqueur de heartbeat local que l'updater surveille pour juger la santé.</param>
    public UpdaterLaunchRequest(
        string targetVersion,
        string stagingDirectory,
        string installDirectory,
        string backupDirectory,
        string serviceName,
        TimeSpan healthTimeout,
        string logPath,
        string statusPath,
        string heartbeatMarkerPath)
    {
        TargetVersion = targetVersion;
        StagingDirectory = stagingDirectory;
        InstallDirectory = installDirectory;
        BackupDirectory = backupDirectory;
        ServiceName = serviceName;
        HealthTimeout = healthTimeout;
        LogPath = logPath;
        StatusPath = statusPath;
        HeartbeatMarkerPath = heartbeatMarkerPath;
    }

    /// <summary>Version installée.</summary>
    public string TargetVersion { get; }

    /// <summary>Dossier des nouveaux binaires vérifiés.</summary>
    public string StagingDirectory { get; }

    /// <summary>Dossier d'installation à remplacer.</summary>
    public string InstallDirectory { get; }

    /// <summary>Dossier de sauvegarde (rollback).</summary>
    public string BackupDirectory { get; }

    /// <summary>Nom du service Windows.</summary>
    public string ServiceName { get; }

    /// <summary>Budget d'attente du redémarrage sain.</summary>
    public TimeSpan HealthTimeout { get; }

    /// <summary>Fichier de log de l'updater.</summary>
    public string LogPath { get; }

    /// <summary>Fichier de statut de l'updater.</summary>
    public string StatusPath { get; }

    /// <summary>Marqueur de heartbeat local surveillé pour juger la santé du redémarrage.</summary>
    public string HeartbeatMarkerPath { get; }
}
