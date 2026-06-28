namespace Liakont.Agent.Core.Storage;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Connaissance des fichiers QUI COMPOSENT la file locale SQLite (F12 §2.3) : la base elle-même
/// (<c>agent-queue.db</c>) et ses annexes du mode WAL, le journal d'écriture anticipée
/// (<c>-wal</c>) et la mémoire partagée d'index (<c>-shm</c>). Centralisé ici pour qu'aucun appelant
/// ne réécrive la liste des suffixes (un oubli laisserait survivre une partie de l'état).
/// <para>
/// PÉRIMÈTRE STRICT : ces opérations ne touchent QUE l'état LOCAL de l'agent sur le poste
/// (<c>%ProgramData%\Liakont\…</c>). Elles n'ouvrent JAMAIS la base SOURCE du client et n'y écrivent
/// rien (CLAUDE.md n°5, lecture seule stricte de la base source).
/// </para>
/// </summary>
public static class LocalQueueFiles
{
    // Annexes du mode WAL de SQLite, accolées au nom du fichier de base (agent-queue.db-wal / -shm).
    private static readonly string[] WalSuffixes = { "-wal", "-shm" };

    /// <summary>
    /// Énumère le chemin de la base et de ses annexes WAL (qu'ils existent ou non sur le disque) :
    /// <paramref name="databasePath"/>, puis <c>&lt;databasePath&gt;-wal</c> et <c>-shm</c>.
    /// </summary>
    /// <param name="databasePath">Chemin du fichier de base SQLite (jamais nul/blanc).</param>
    public static IReadOnlyList<string> Enumerate(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Le chemin de la base locale est requis.", nameof(databasePath));
        }

        var paths = new List<string>(WalSuffixes.Length + 1) { databasePath };
        foreach (string suffix in WalSuffixes)
        {
            paths.Add(databasePath + suffix);
        }

        return paths;
    }

    /// <summary>
    /// Supprime la file locale et ses annexes WAL si elles existent (état local de l'agent UNIQUEMENT).
    /// Idempotent : un fichier déjà absent n'est pas une erreur. Renvoie le nombre de fichiers
    /// effectivement supprimés.
    /// <para>
    /// Utilisé à la DÉSINSTALLATION du service (BUG-2) : sans cette purge, le filigrane d'extraction
    /// (<see cref="LocalQueue.ExtractionWatermarkKey"/>, table <c>agent_state</c>) survivrait dans
    /// <c>agent-queue.db</c> et une réinstallation reprendrait l'ancien filigrane au lieu de repartir
    /// d'un état vierge.
    /// </para>
    /// </summary>
    /// <param name="databasePath">Chemin du fichier de base SQLite (jamais nul/blanc).</param>
    public static int Purge(string databasePath)
    {
        int removed = 0;
        foreach (string path in Enumerate(databasePath))
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                removed++;
            }
        }

        return removed;
    }
}
