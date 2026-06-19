namespace Liakont.Modules.Signature.Tests.Unit.TestDoubles.OnSite;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.SupportTrace.Contracts;

/// <summary>Double de <see cref="ISupportTraceStore"/> : <see cref="ReadAsync"/> renvoie les octets configurés (ou null).</summary>
internal sealed class FakeSupportTraceStore : ISupportTraceStore
{
    private readonly byte[]? _artifact;

    public FakeSupportTraceStore(byte[]? artifact) => _artifact = artifact;

    public Task<byte[]?> ReadAsync(string tenantId, Guid documentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_artifact);

    public Task WriteAsync(string tenantId, Guid documentId, ReadOnlyMemory<byte> facturX, DateTimeOffset recordedAtUtc, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<int> PurgeOlderThanAsync(string tenantId, DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
