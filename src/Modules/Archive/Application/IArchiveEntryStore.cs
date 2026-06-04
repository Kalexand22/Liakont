namespace Liakont.Modules.Archive.Application;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Persistance des entrées du coffre dans <c>documents.archive_entries</c> (table créée par TRK01,
/// « alimentée par TRK05 »), tenant-scopée par la connexion. Garante de l'ordonnancement STRICT et de
/// l'atomicité du maillon de chaîne : <see cref="ReserveAsync"/> sérialise les ajouts du tenant sous
/// verrou consultatif, lit la tête, calcule le <c>chain_hash</c> et insère la ligne — SANS aucune E/S
/// coffre. L'appelant écrit les artefacts du coffre hors de cette transaction ; le manifest est écrit
/// APRÈS commit, dérivé du (chain_hash, archived_utc) committé, donc rejouable à l'identique en cas de
/// reprise (idempotent write-once).
/// </summary>
public interface IArchiveEntryStore
{
    /// <summary>
    /// Réserve (ou retrouve) l'entrée de chaîne pour ce <paramref name="packagePath"/>, atomiquement sous
    /// verrou de sérialisation — SANS aucune E/S coffre. Idempotent : un second appel pour le même
    /// packagePath retourne l'entrée déjà scellée (aucune ligne dupliquée). L'appelant écrit les artefacts
    /// du coffre (contenu AVANT, manifest APRÈS) hors de cette transaction ; le manifest est dérivé du
    /// (chain_hash, archived_utc) committé, donc rejouable à l'identique en cas de reprise.
    /// </summary>
    /// <param name="documentId">Document rattaché (FK).</param>
    /// <param name="packagePath">Chemin (relatif au tenant) du manifest de l'entrée — clé d'idempotence.</param>
    /// <param name="packageHash">Empreinte du paquet/addendum (entry_hash) — stockée en <c>package_hash</c>.</param>
    Task<ArchiveEntryRecord> ReserveAsync(
        Guid documentId,
        string packagePath,
        string packageHash,
        CancellationToken cancellationToken = default);

    /// <summary>Lit toute la chaîne du tenant, dans l'ordre d'ajout (déterministe).</summary>
    Task<IReadOnlyList<ArchiveEntryRecord>> GetChainAsync(CancellationToken cancellationToken = default);
}
