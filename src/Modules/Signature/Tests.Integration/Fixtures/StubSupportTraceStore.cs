namespace Liakont.Modules.Signature.Tests.Integration.Fixtures;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.SupportTrace.Contracts;

/// <summary>Stub de <see cref="ISupportTraceStore"/> pour les tests d'intégration : ReadAsync renvoie les octets configurés.</summary>
internal sealed class StubSupportTraceStore : ISupportTraceStore
{
    private readonly byte[]? _artifact;

    public StubSupportTraceStore(byte[]? artifact) => _artifact = artifact;

    public Task<byte[]?> ReadAsync(string tenantId, Guid documentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_artifact);

    public Task WriteAsync(string tenantId, Guid documentId, ReadOnlyMemory<byte> facturX, DateTimeOffset recordedAtUtc, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<int> PurgeOlderThanAsync(string tenantId, DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
