namespace Liakont.Modules.Ingestion.Tests.Integration.Doubles;

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Staging.Contracts;

/// <summary>
/// Double de test du magasin de staging (PIP00) : enregistre les contenus stagés par <c>DocumentId</c> et
/// expose un hook <see cref="OnWriteAsync"/> exécuté AVANT l'enregistrement — utilisé pour observer
/// l'invariant d'ordre de l'intake (le blob est écrit avant le commit) ou injecter un échec d'écriture.
/// </summary>
internal sealed class RecordingPayloadStagingStore : IPayloadStagingStore
{
    private readonly ConcurrentDictionary<Guid, string> _staged = new();

    /// <summary>Hook appelé au début de chaque écriture (avant l'enregistrement) ; peut lever pour simuler un échec.</summary>
    public Func<StagedPayloadKey, Task>? OnWriteAsync { get; set; }

    public PayloadStagingStoreCapabilities Capabilities => PayloadStagingStoreCapabilities.None;

    /// <summary>Nombre total d'écritures TENTÉES (incrémenté même si le hook lève).</summary>
    public int WriteAttempts { get; private set; }

    /// <summary>Nombre d'entrées effectivement stagées.</summary>
    public int Count => _staged.Count;

    public async Task WriteAsync(StagedPayloadKey key, string canonicalJson, CancellationToken cancellationToken = default)
    {
        WriteAttempts++;
        if (OnWriteAsync is not null)
        {
            await OnWriteAsync(key);
        }

        _staged[key.DocumentId] = canonicalJson;
    }

    public Task<string> ReadAsync(StagedPayloadKey key, CancellationToken cancellationToken = default) =>
        _staged.TryGetValue(key.DocumentId, out string? json)
            ? Task.FromResult(json)
            : throw StagedPayloadNotFoundException.ForKey(key);

    public Task<bool> ExistsAsync(StagedPayloadKey key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_staged.ContainsKey(key.DocumentId));

    public Task PurgeAsync(StagedPayloadKey key, CancellationToken cancellationToken = default)
    {
        _staged.TryRemove(key.DocumentId, out _);
        return Task.CompletedTask;
    }
}
