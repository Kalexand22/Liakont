namespace Liakont.Modules.Archive.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Domain;

/// <summary>Réplique en mémoire d'<see cref="IArchiveAnchorStore"/> : append-only, idempotence par (tête, méthode) gérée par l'appelant.</summary>
internal sealed class FakeArchiveAnchorStore : IArchiveAnchorStore
{
    private readonly List<ArchiveAnchorRecord> _records = [];
    private long _tick;

    public IReadOnlyList<ArchiveAnchorRecord> Records => _records;

    public Task<ArchiveAnchorRecord> AppendAsync(
        Guid chainHeadEntryId,
        string chainHeadHash,
        TimestampAnchorMethod method,
        string status,
        string? proofPath,
        DateTimeOffset? anchoredUtc,
        CancellationToken cancellationToken = default)
    {
        var record = new ArchiveAnchorRecord(
            Guid.NewGuid(),
            chainHeadEntryId,
            chainHeadHash,
            method,
            status,
            proofPath,
            anchoredUtc,
            DateTimeOffset.UnixEpoch.AddTicks(_tick++));
        _records.Add(record);
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<ArchiveAnchorRecord>> GetAnchorsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult((IReadOnlyList<ArchiveAnchorRecord>)_records.ToList());

    public Task<ArchiveAnchorRecord?> GetLatestForHeadAsync(
        string chainHeadHash,
        TimestampAnchorMethod method,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_records.LastOrDefault(r => r.ChainHeadHash == chainHeadHash && r.Method == method));
}
