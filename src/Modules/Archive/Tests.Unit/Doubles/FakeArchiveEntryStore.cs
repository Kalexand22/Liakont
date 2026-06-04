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
/// pour exercer la vraie logique de chaînage de <c>ArchiveService</c> sans base. Les ajouts sont
/// sérialisés (un verrou) comme côté production.
/// </summary>
public sealed class FakeArchiveEntryStore : IArchiveEntryStore
{
    private readonly List<ArchiveEntryRecord> _records = [];

    public IReadOnlyList<ArchiveEntryRecord> Records => _records;

    public async Task<ArchiveEntryRecord> AppendAsync(
        Guid documentId,
        string packageHash,
        Func<ArchiveSealContext, CancellationToken, Task<string>> writeArtifacts,
        CancellationToken cancellationToken = default)
    {
        string? previousChain = _records.Count == 0 ? null : _records[^1].ChainHash;
        DateTimeOffset archivedUtc = _records.Count == 0
            ? DateTimeOffset.UnixEpoch
            : _records[^1].ArchivedUtc.AddTicks(10);

        string chainHash = HashChain.Next(previousChain, packageHash);
        string packagePath = await writeArtifacts(new ArchiveSealContext(chainHash, archivedUtc), cancellationToken);

        var record = new ArchiveEntryRecord(Guid.NewGuid(), documentId, packagePath, packageHash, chainHash, archivedUtc);
        _records.Add(record);
        return record;
    }

    public Task<IReadOnlyList<ArchiveEntryRecord>> GetChainAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult((IReadOnlyList<ArchiveEntryRecord>)_records.ToList());
}
