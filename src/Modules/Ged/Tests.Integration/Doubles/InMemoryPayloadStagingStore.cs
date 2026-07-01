namespace Liakont.Modules.Ged.Tests.Integration.Doubles;

using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Staging.Contracts;

/// <summary>
/// Magasin de staging EN MÉMOIRE (double de test) partagé entre le handler d'ingestion GED (qui stage le pivot
/// canonique) et le consommateur (qui le relit) — F19 §2.4/ADR-0014. Thread-safe (test concurrent). La clé
/// <see cref="StagedPayloadKey"/> est un record (égalité par valeur), utilisable directement en clé de dictionnaire.
/// </summary>
internal sealed class InMemoryPayloadStagingStore : IPayloadStagingStore
{
    private readonly ConcurrentDictionary<StagedPayloadKey, string> _entries = new();

    public PayloadStagingStoreCapabilities Capabilities => PayloadStagingStoreCapabilities.None;

    public Task WriteAsync(StagedPayloadKey key, string canonicalJson, CancellationToken cancellationToken = default)
    {
        _entries[key] = canonicalJson;
        return Task.CompletedTask;
    }

    public Task<string> ReadAsync(StagedPayloadKey key, CancellationToken cancellationToken = default) =>
        _entries.TryGetValue(key, out var content)
            ? Task.FromResult(content)
            : throw StagedPayloadNotFoundException.ForKey(key);

    public Task<bool> ExistsAsync(StagedPayloadKey key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_entries.ContainsKey(key));

    public Task PurgeAsync(StagedPayloadKey key, CancellationToken cancellationToken = default)
    {
        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
