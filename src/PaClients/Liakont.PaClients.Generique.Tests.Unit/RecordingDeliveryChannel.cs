namespace Liakont.PaClients.Generique.Tests.Unit;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Canal de livraison FACTICE : enregistre la dernière demande reçue (preuve de transmission) et permet
/// de simuler un échec de transport. Sert à prouver que le plug-in TRANSPORTE l'artefact reçu sans le
/// régénérer, et qu'il sélectionne le bon canal par son <see cref="DocumentDeliveryMethod"/>.
/// </summary>
internal sealed class RecordingDeliveryChannel(DocumentDeliveryMethod method, bool throwOnDeliver = false)
    : IDocumentDeliveryChannel
{
    public DocumentDeliveryMethod Method { get; } = method;

    public DocumentDeliveryRequest? LastRequest { get; private set; }

    public int DeliverCount { get; private set; }

    public Task DeliverAsync(DocumentDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        DeliverCount++;
        LastRequest = request;
        if (throwOnDeliver)
        {
            throw new InvalidOperationException("Échec de transport simulé.");
        }

        return Task.CompletedTask;
    }
}
