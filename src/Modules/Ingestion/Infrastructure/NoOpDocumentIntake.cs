namespace Liakont.Modules.Ingestion.Infrastructure;

using Liakont.Modules.Ingestion.Contracts;

/// <summary>
/// Implémentation par défaut, SÛRE et provisoire, du port <see cref="IDocumentIntake"/> : tant que le
/// module <c>Documents</c> (TRK01/TRK02) n'est pas livré, il n'existe aucun store de documents à
/// alimenter. Ce shim ne crée donc rien et n'a aucun effet (idempotent par construction).
/// </summary>
/// <remarks>
/// Ce n'est PAS un faux-vert : le déclencheur DURABLE du traitement aval est l'événement
/// d'intégration <see cref="Contracts.Events.DocumentReceivedV1"/> publié via l'outbox (consommé par
/// PIP01), et l'entrée d'anti-doublon est persistée par l'ingestion. Le câblage réel de la création du
/// document en état <c>Detected</c> (et le choix du modèle de cohérence définitif) arrive avec TRK02,
/// qui remplace cette implémentation. Même motif que <c>SafeDefaultAgentConfigurationProvider</c> (PIV05).
/// </remarks>
internal sealed class NoOpDocumentIntake : IDocumentIntake
{
    public Task RegisterDetectedDocumentAsync(DetectedDocumentIntake input, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    // Sans store de documents, la notion de « rangé » n'a pas de sens : on répond TRUE (« rien à re-ranger »)
    // pour que l'affinage du dédoublonnage (ADR-0012) ne déclenche aucun re-staging / re-rangement inutile tant
    // que le module Documents n'est pas câblé. Le câblage réel (DocumentIntake) renvoie l'existence effective.
    public Task<bool> IsDocumentRangedAsync(Guid documentId, string tenantId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}
