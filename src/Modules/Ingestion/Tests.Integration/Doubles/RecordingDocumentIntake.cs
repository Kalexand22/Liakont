namespace Liakont.Modules.Ingestion.Tests.Integration.Doubles;

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ingestion.Contracts;

/// <summary>
/// Espion du port <see cref="IDocumentIntake"/> : enregistre les documents que l'ingestion ferait créer en
/// état <c>Detected</c>, et SIMULE le store de documents (les identifiants rangés avec succès sont mémorisés,
/// <see cref="IsDocumentRangedAsync"/> les reflète). Permet de vérifier que seuls les documents ACCEPTÉS
/// déclenchent la création, et — via <see cref="FailNextRegistrations"/> — de simuler un hoquet de la base
/// tenant (rangement échoué) pour l'AFFINAGE DU DÉDOUBLONNAGE (ADR-0012 : reçu mais non rangé → re-rangé au renvoi).
/// </summary>
internal sealed class RecordingDocumentIntake : IDocumentIntake
{
    private readonly ConcurrentDictionary<Guid, byte> _ranged = new();
    private int _failuresRemaining;

    public ConcurrentQueue<DetectedDocumentIntake> Calls { get; } = new();

    /// <summary>Force l'échec des <paramref name="count"/> prochains rangements (hoquet base tenant simulé).</summary>
    public void FailNextRegistrations(int count) => Interlocked.Exchange(ref _failuresRemaining, count);

    public Task RegisterDetectedDocumentAsync(DetectedDocumentIntake input, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue(input);

        // Decrement renvoie la valeur après décrément : si elle reste >= 0, il restait des échecs à consommer.
        if (Interlocked.Decrement(ref _failuresRemaining) >= 0)
        {
            throw new InvalidOperationException("Échec simulé du rangement (hoquet de la base tenant).");
        }

        _ranged[input.DocumentId] = 1;
        return Task.CompletedTask;
    }

    public Task<bool> IsDocumentRangedAsync(Guid documentId, string tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_ranged.ContainsKey(documentId));
}
