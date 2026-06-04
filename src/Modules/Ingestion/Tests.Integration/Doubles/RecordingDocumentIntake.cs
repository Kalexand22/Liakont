namespace Liakont.Modules.Ingestion.Tests.Integration.Doubles;

using System.Collections.Concurrent;
using Liakont.Modules.Ingestion.Contracts;

/// <summary>
/// Espion du port <see cref="IDocumentIntake"/> : enregistre les documents que l'ingestion ferait
/// créer en état <c>Detected</c> (le vrai module Documents arrive avec TRK02). Permet de vérifier que
/// seuls les documents ACCEPTÉS déclenchent la création, avec l'identifiant partagé.
/// </summary>
internal sealed class RecordingDocumentIntake : IDocumentIntake
{
    public ConcurrentQueue<DetectedDocumentIntake> Calls { get; } = new();

    public Task RegisterDetectedDocumentAsync(DetectedDocumentIntake input, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue(input);
        return Task.CompletedTask;
    }
}
