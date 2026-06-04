namespace Liakont.Modules.Archive.Tests.Unit.Doubles;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Domain;

/// <summary>
/// Réplique fidèle EN MÉMOIRE de <see cref="IArchiveEntryStore"/> : même calcul de chaîne
/// (<see cref="HashChain.Next"/>) et même horodatage strictement croissant que le store PostgreSQL réel,
/// pour exercer la vraie logique de chaînage de <c>ArchiveService</c> sans base. Idempotent par
/// <c>packagePath</c> : un second appel pour le même chemin retourne l'entrée existante.
/// </summary>
public sealed class FakeArchiveEntryStore : IArchiveEntryStore
{
    private readonly List<ArchiveEntryRecord> _records = [];

    public IReadOnlyList<ArchiveEntryRecord> Records => _records;

    public Task<ArchiveEntryRecord> ReserveAsync(
        Guid documentId,
        string packagePath,
        string packageHash,
        CancellationToken cancellationToken = default)
    {
        // Idempotence : retourner l'entrée existante si le chemin est déjà réservé.
        ArchiveEntryRecord? existing = _records.FirstOrDefault(r => r.PackagePath == packagePath);
        if (existing is not null)
        {
            return Task.FromResult(existing);
        }

        string? previousChain = _records.Count == 0 ? null : _records[^1].ChainHash;
        DateTimeOffset archivedUtc = _records.Count == 0
            ? DateTimeOffset.UnixEpoch
            : _records[^1].ArchivedUtc.AddTicks(10);

        string chainHash = HashChain.Next(previousChain, packageHash);
        var record = new ArchiveEntryRecord(Guid.NewGuid(), documentId, packagePath, packageHash, chainHash, archivedUtc);
        _records.Add(record);
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<ArchiveEntryRecord>> GetChainAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult((IReadOnlyList<ArchiveEntryRecord>)_records.ToList());
}
