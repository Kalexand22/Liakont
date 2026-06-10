namespace Liakont.Agent.Core;

using System;
using System.IO;
using System.Threading;

/// <summary>
/// Emplacements de référence de l'agent sur le poste client (F12 §2.3, §2.4), dérivés de
/// l'INSTANCE courante du processus (multi-instances, OPS05 pt 5 — décision 2026-06-10).
/// <para>
/// Instance par défaut : tout vit sous <c>C:\ProgramData\Liakont\</c>
/// (<see cref="Environment.SpecialFolder.CommonApplicationData"/>) — chemins identiques à l'agent
/// mono-instance historique. Instance nommée : sous <c>C:\ProgramData\Liakont\&lt;nom&gt;\</c>.
/// Le répertoire est lisible et inscriptible par le service Windows (LocalSystem) ET par
/// l'intégrateur qui lance le CLI.
/// </para>
/// <para>
/// <see cref="Initialize"/> est appelé UNE FOIS au démarrage du processus (Main du service, du mode
/// console et du CLI), AVANT tout usage des chemins ; un processus ne sert qu'une instance — toute
/// tentative de bascule est une erreur de programmation et lève.
/// </para>
/// <para>
/// EXIGENCE ACL (posée par l'installeur OPS05) : le répertoire <see cref="RootDirectory"/> doit
/// accorder l'écriture au compte du service ET au groupe des intégrateurs — les fichiers
/// <c>-wal</c>/<c>-shm</c> de SQLite exigent l'écriture pour TOUS les processus qui ouvrent la base
/// (service planifié + CLI manuel partagent <see cref="DatabasePath"/>). Le verrou de sérialisation
/// des runs est porté par <see cref="Hosting.InterProcessRunLock"/> (mutex nommé PAR INSTANCE,
/// <see cref="AgentInstance.RunMutexName"/>), pas par le FS.
/// </para>
/// </summary>
public static class AgentPaths
{
    private static AgentInstance _current = AgentInstance.Default;
    private static int _initialized;

    /// <summary>Instance servie par ce processus (Default tant que <see cref="Initialize"/> n'a pas été appelé).</summary>
    public static AgentInstance Current => _current;

    /// <summary>Racine des données de l'instance courante (voir <see cref="AgentInstance.DataDirectory"/>).</summary>
    public static string RootDirectory => _current.DataDirectory;

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

    /// <summary>
    /// Fixe l'instance du processus. Idempotent pour une même instance ; lève si une instance
    /// DIFFÉRENTE a déjà été fixée (un processus = une instance, jamais de bascule à chaud).
    /// </summary>
    public static void Initialize(AgentInstance instance)
    {
        if (instance is null)
        {
            throw new ArgumentNullException(nameof(instance));
        }

        if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
        {
            _current = instance;
            return;
        }

        if (!string.Equals(_current.Name, instance.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"AgentPaths est déjà initialisé pour l'instance « {_current.Name} » — " +
                $"impossible de basculer vers « {instance.Name} » dans le même processus.");
        }
    }

    /// <summary>Réinitialisation réservée aux tests (état statique de processus).</summary>
    internal static void ResetForTesting()
    {
        _current = AgentInstance.Default;
        Interlocked.Exchange(ref _initialized, 0);
    }
}
