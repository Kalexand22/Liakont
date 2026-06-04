namespace Liakont.Modules.Archive.Application;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Domain;

/// <summary>
/// Persistance des ancrages temporels dans <c>documents.archive_anchors</c> (table créée par la migration
/// V006, TRK06), tenant-scopée par la connexion. Table APPEND-ONLY / WORM (mêmes triggers anti
/// UPDATE/DELETE/TRUNCATE que <c>archive_entries</c>, CLAUDE.md n°4) : ce store n'expose QUE des
/// insertions et des lectures. Elle INDEXE les preuves stockées dans le coffre (comme
/// <see cref="IArchiveEntryStore"/> indexe les paquets), pour que le vérifieur retrouve les preuves sans
/// énumérer le coffre.
/// </summary>
public interface IArchiveAnchorStore
{
    /// <summary>Insère une ligne d'ancrage (append-only). Retourne la ligne scellée.</summary>
    Task<ArchiveAnchorRecord> AppendAsync(
        Guid chainHeadEntryId,
        string chainHeadHash,
        TimestampAnchorMethod method,
        string status,
        string? proofPath,
        DateTimeOffset? anchoredUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Lit tous les ancrages du tenant, du plus ancien au plus récent.</summary>
    Task<IReadOnlyList<ArchiveAnchorRecord>> GetAnchorsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Dernier ancrage pour une tête de chaîne donnée et une méthode donnée (clé d'idempotence du job
    /// quotidien : ne pas réancrer une tête déjà ancrée), ou <c>null</c>.
    /// </summary>
    Task<ArchiveAnchorRecord?> GetLatestForHeadAsync(
        string chainHeadHash,
        TimestampAnchorMethod method,
        CancellationToken cancellationToken = default);
}
