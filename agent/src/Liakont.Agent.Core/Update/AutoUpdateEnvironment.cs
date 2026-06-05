namespace Liakont.Agent.Core.Update;

using System;

/// <summary>
/// Paramètres d'environnement de l'auto-update sur le poste (chemins, nom de service, version
/// courante, budget de santé). Regroupe ce que le coordinateur doit connaître pour préparer la mise à
/// jour et construire la requête de l'updater (ADR-0013). Fourni par la racine de composition.
/// </summary>
public sealed class AutoUpdateEnvironment
{
    /// <summary>Crée un environnement d'auto-update.</summary>
    /// <param name="currentVersion">Version de l'agent actuellement installé (garde anti-downgrade).</param>
    /// <param name="serviceName">Nom du service Windows à arrêter/redémarrer.</param>
    /// <param name="installDirectory">Dossier d'installation des binaires de l'agent.</param>
    /// <param name="workRootDirectory">Racine de travail (téléchargements, extraction, sauvegarde) hors dossier d'installation.</param>
    /// <param name="updaterLogPath">Fichier de log de l'updater détaché.</param>
    /// <param name="statusPath">Fichier de statut partagé (écrit par l'agent et l'updater).</param>
    /// <param name="heartbeatMarkerPath">Marqueur de heartbeat local surveillé pour juger la santé du redémarrage.</param>
    /// <param name="healthTimeout">Budget d'attente du redémarrage sain avant rollback.</param>
    public AutoUpdateEnvironment(
        string currentVersion,
        string serviceName,
        string installDirectory,
        string workRootDirectory,
        string updaterLogPath,
        string statusPath,
        string heartbeatMarkerPath,
        TimeSpan healthTimeout)
    {
        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            throw new ArgumentException("La version courante est requise.", nameof(currentVersion));
        }

        CurrentVersion = currentVersion;
        ServiceName = serviceName;
        InstallDirectory = installDirectory;
        WorkRootDirectory = workRootDirectory;
        UpdaterLogPath = updaterLogPath;
        StatusPath = statusPath;
        HeartbeatMarkerPath = heartbeatMarkerPath;
        HealthTimeout = healthTimeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(5) : healthTimeout;
    }

    /// <summary>Version de l'agent actuellement installé.</summary>
    public string CurrentVersion { get; }

    /// <summary>Nom du service Windows.</summary>
    public string ServiceName { get; }

    /// <summary>Dossier d'installation des binaires.</summary>
    public string InstallDirectory { get; }

    /// <summary>Racine de travail (hors dossier d'installation).</summary>
    public string WorkRootDirectory { get; }

    /// <summary>Fichier de log de l'updater.</summary>
    public string UpdaterLogPath { get; }

    /// <summary>Fichier de statut partagé.</summary>
    public string StatusPath { get; }

    /// <summary>Marqueur de heartbeat local.</summary>
    public string HeartbeatMarkerPath { get; }

    /// <summary>Budget d'attente du redémarrage sain.</summary>
    public TimeSpan HealthTimeout { get; }
}
