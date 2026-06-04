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
}
