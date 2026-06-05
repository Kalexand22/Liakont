namespace Liakont.Agent.Core;

using System;
using System.IO;

/// <summary>
/// Emplacements de référence de l'agent sur le poste client (F12 §2.3, §2.4).
/// <para>
/// Tout vit sous <c>C:\ProgramData\Liakont\</c> (<see cref="Environment.SpecialFolder.CommonApplicationData"/>),
/// lisible et inscriptible par le service Windows (LocalSystem) ET par l'intégrateur qui lance le CLI.
/// </para>
/// <para>
/// EXIGENCE ACL (posée par l'installeur OPS05) : le répertoire <see cref="RootDirectory"/> doit
/// accorder l'écriture au compte du service ET au groupe des intégrateurs — les fichiers
/// <c>-wal</c>/<c>-shm</c> de SQLite exigent l'écriture pour TOUS les processus qui ouvrent la base
/// (service planifié + CLI manuel partagent <see cref="DatabasePath"/>). Le verrou de sérialisation
/// des runs est porté par <see cref="Hosting.InterProcessRunLock"/> (mutex nommé), pas par le FS.
/// </para>
/// </summary>
public static class AgentPaths
{
    /// <summary>Racine des données de l'agent : <c>C:\ProgramData\Liakont</c>.</summary>
    public static string RootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Liakont");

    /// <summary>Fichier de configuration de l'agent (<c>agent.json</c>, F12 §2.4).</summary>
    public static string ConfigPath => Path.Combine(RootDirectory, "agent.json");

    /// <summary>Base SQLite de la file locale (tampon technique, F12 §2.3).</summary>
    public static string DatabasePath => Path.Combine(RootDirectory, "agent-queue.db");

    /// <summary>Répertoire des journaux fichiers de l'agent (rotation 90 jours).</summary>
    public static string LogDirectory => Path.Combine(RootDirectory, "logs");

    /// <summary>
    /// Clé PUBLIQUE de signature des manifestes d'auto-update (XML), provisionnée par l'installeur
    /// (OPS05/F13). Jamais embarquée en dur dans le code (CLAUDE.md n°7, ADR-0013) ; absente = aucune
    /// mise à jour acceptée (fail-closed).
    /// </summary>
    public static string UpdateSigningKeyPath => Path.Combine(RootDirectory, "update-signing.pubkey.xml");

    /// <summary>Fichier de statut de la dernière tentative d'auto-update (signalement heartbeat, AGT04).</summary>
    public static string UpdateStatusPath => Path.Combine(RootDirectory, "update-status.json");

    /// <summary>Racine de travail de l'auto-update (téléchargement, extraction, sauvegarde) — hors dossier d'installation.</summary>
    public static string UpdateWorkDirectory => Path.Combine(RootDirectory, "update-work");

    /// <summary>
    /// Marqueur de heartbeat local : touché par l'agent à chaque heartbeat sain, surveillé par
    /// l'updater détaché pour juger qu'une nouvelle version a bien redémarré (sinon rollback, ADR-0013).
    /// </summary>
    public static string HeartbeatMarkerPath => Path.Combine(RootDirectory, "heartbeat.marker");
}
