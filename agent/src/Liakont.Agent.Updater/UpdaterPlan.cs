namespace Liakont.Agent.Updater;

using System;

/// <summary>
/// Plan d'un remplacement de binaires (ADR-0013), reçu en arguments de ligne de commande. Décrit
/// QUOI installer, OÙ, comment sauvegarder, et le budget d'attente du redémarrage sain avant rollback.
/// </summary>
public sealed class UpdaterPlan
{
    /// <summary>Crée un plan d'updater.</summary>
    /// <param name="targetVersion">Version installée (journalisée + reportée dans le statut).</param>
    /// <param name="stagingDirectory">Dossier des nouveaux binaires vérifiés.</param>
    /// <param name="installDirectory">Dossier d'installation à remplacer.</param>
    /// <param name="backupDirectory">Dossier de sauvegarde des binaires courants (rollback).</param>
    /// <param name="serviceName">Nom du service Windows.</param>
    /// <param name="healthTimeout">Budget d'attente du redémarrage sain.</param>
    /// <param name="heartbeatMarkerPath">Marqueur de heartbeat local surveillé pour juger la santé.</param>
    /// <param name="statusPath">Fichier de statut écrit en fin de cycle (relu par l'agent).</param>
    public UpdaterPlan(
        string targetVersion,
        string stagingDirectory,
        string installDirectory,
        string backupDirectory,
        string serviceName,
        TimeSpan healthTimeout,
        string heartbeatMarkerPath,
        string statusPath)
    {
        TargetVersion = targetVersion;
        StagingDirectory = stagingDirectory;
        InstallDirectory = installDirectory;
        BackupDirectory = backupDirectory;
        ServiceName = serviceName;
        HealthTimeout = healthTimeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(5) : healthTimeout;
        HeartbeatMarkerPath = heartbeatMarkerPath;
        StatusPath = statusPath;
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

    /// <summary>Marqueur de heartbeat local.</summary>
    public string HeartbeatMarkerPath { get; }

    /// <summary>Fichier de statut écrit en fin de cycle.</summary>
    public string StatusPath { get; }
}
